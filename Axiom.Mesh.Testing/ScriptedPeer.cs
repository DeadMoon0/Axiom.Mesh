using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Discovery;
using Axiom.Mesh.Protocol;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Testing;

/// <summary>
/// A peer a test drives by hand: it connects to a node of a <see cref="TestMesh"/> in memory with any published
/// identity, runs the TLS handshake as a node's client side would (or deliberately not: without a certificate, without
/// an ALPN), and then speaks raw AMP/1, message by message, so a test can send what a real node never would and see
/// exactly what the node answers.
/// </summary>
public sealed class ScriptedPeer : IAsyncDisposable
{
    private readonly SslStream tls;
    private readonly X509Certificate2 certificate;
    private readonly TimeProvider clock;
    private Connection? connection;
    private Session<MeshMessage, MeshMessage>? session;
    private Session<PairMessage, PairMessage>? pairing;

    private ScriptedPeer(TestIdentity identity, SslStream tls, X509Certificate2 certificate, TimeProvider clock)
    {
        Identity = identity;
        this.tls = tls;
        this.certificate = certificate;
        this.clock = clock;
    }

    /// <summary>The published device the peer presents.</summary>
    public TestIdentity Identity { get; }

    /// <summary>Why the handshake failed, from this side; <see langword="null"/> when it completed.</summary>
    public Exception? HandshakeFailure { get; private set; }

    /// <summary>Whether the handshake completed from this side (TLS 1.3 lets a client finish before the server judged it).</summary>
    public bool HandshakeCompleted => HandshakeFailure is null;

    /// <summary>The negotiated ALPN, when the handshake completed and one was negotiated.</summary>
    public string? NegotiatedAlpn { get; private set; }

    /// <summary>The device the node's certificate names.</summary>
    public DeviceId RemoteDevice { get; private set; }

    /// <summary>Connects to a node and runs the TLS handshake; a refused handshake is recorded, not thrown.</summary>
    /// <param name="target">The node.</param>
    /// <param name="identity">The device the peer presents.</param>
    /// <param name="options">From where and under which ALPN.</param>
    /// <param name="cancellationToken">Cancels connecting.</param>
    /// <returns>The peer, its handshake done or failed.</returns>
    public static Task<ScriptedPeer> ConnectAsync(TestNode target, TestIdentity identity, ScriptedPeerOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        return ConnectAsync(target.Node.ConnectInMemory(options.From), identity, target.Environment.Clock, options, cancellationToken);
    }

    /// <summary>
    /// Runs the TLS handshake as a node's client side would over a connection the caller opened to a node's mesh
    /// listener (in memory, or a TCP connection to a node on sockets), and speaks raw AMP/1 over it afterwards. A refused
    /// handshake is recorded, not thrown.
    /// </summary>
    /// <param name="transport">The open connection; the peer owns it from now on.</param>
    /// <param name="identity">The device the peer presents.</param>
    /// <param name="clock">The clock of the node's mesh: the certificate's validity and the session's timers run on it.</param>
    /// <param name="options">The ALPNs offered; <see cref="ScriptedPeerOptions.From"/> is the caller's, who opened the connection.</param>
    /// <param name="cancellationToken">Cancels the handshake.</param>
    /// <returns>The peer, its handshake done or failed.</returns>
    public static async Task<ScriptedPeer> ConnectAsync(Stream transport, TestIdentity identity, TimeProvider clock, ScriptedPeerOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        X509Certificate2 certificate = CertificateOf(identity, clock);
        var peer = new ScriptedPeer(identity, new SslStream(transport, leaveInnerStreamOpen: false), certificate, clock);
        var authentication = new SslClientAuthenticationOptions
        {
            TargetHost = "axiom-mesh",
            EnabledSslProtocols = SslProtocols.Tls13,
            AllowTlsResume = false,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            ApplicationProtocols = options.Alpn.Count == 0 ? null : [.. options.Alpn.Select(a => new SslApplicationProtocol(a))],
            ClientCertificateContext = SslStreamCertificateContext.Create(certificate, null, offline: true),
            RemoteCertificateValidationCallback = peer.Remember,
        };
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            await peer.tls.AuthenticateAsClientAsync(authentication, linked.Token).ConfigureAwait(false);
            peer.NegotiatedAlpn = peer.tls.NegotiatedApplicationProtocol == default ? null : peer.tls.NegotiatedApplicationProtocol.ToString();
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            peer.HandshakeFailure = ex;
        }

        return peer;
    }

    /// <summary>
    /// Waits to be dialled: puts the identity's claim at <paramref name="address"/> on the mesh's discovery board, takes
    /// the first connection a node opens to it and runs the TLS handshake as a node's server side would, under
    /// <c>axiom-mesh/1</c>. The connection is then one the node dialled (SPEC.md §4.5), which a connecting peer cannot make.
    /// </summary>
    /// <param name="mesh">The mesh whose nodes may dial.</param>
    /// <param name="identity">The device the peer presents.</param>
    /// <param name="address">Where it claims to listen.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The peer, its handshake done or failed.</returns>
    /// <exception cref="TestMeshException">No node dialled within the deadline.</exception>
    public static async Task<ScriptedPeer> AcceptAsync(TestMesh mesh, TestIdentity identity, IPEndPoint address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(address);
        var accepted = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        mesh.Network.Listen(address, _ =>
        {
            (Stream mine, Stream theirs) = InMemoryDuplex.Create();
            if (!accepted.TrySetResult(mine))
                throw new InvalidOperationException("The scripted peer takes one connection.");
            return theirs;
        });
        mesh.Network.Announce(new Announcement(identity.DeviceId, identity.UserId, address, DiscoverySource.Mdns));

        Stream stream;
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            stream = await accepted.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TestMeshException(
                $"No node dialled the scripted peer {identity.Name} at {address} within {TestMesh.Deadline.TotalSeconds:0} s.",
                "Make sure a node knows the identity's user, and read its facts for the dial policy's decision.");
        }
        finally
        {
            mesh.Network.Unlisten(address);
            mesh.Network.Withdraw(identity.DeviceId);
        }

        X509Certificate2 certificate = CertificateOf(identity, mesh.Clock);
        var peer = new ScriptedPeer(identity, new SslStream(stream, leaveInnerStreamOpen: false), certificate, mesh.Clock);
        var authentication = new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            ClientCertificateRequired = true,
            EnabledSslProtocols = SslProtocols.Tls13,
            AllowTlsResume = false,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            ApplicationProtocols = [new SslApplicationProtocol(Amp.Alpn)],
            RemoteCertificateValidationCallback = peer.Remember,
        };
        try
        {
            await peer.tls.AuthenticateAsServerAsync(authentication, linked.Token).ConfigureAwait(false);
            peer.NegotiatedAlpn = peer.tls.NegotiatedApplicationProtocol == default ? null : peer.tls.NegotiatedApplicationProtocol.ToString();
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            peer.HandshakeFailure = ex;
        }

        return peer;
    }

    /// <summary>The <c>Hello</c> this peer's identity would send (SPEC.md §7.1).</summary>
    /// <param name="rosterSeq">The seq of the peer's user's Roster it claims to hold.</param>
    /// <returns>The message.</returns>
    public Hello Hello(ulong rosterSeq) => new(Identity.DeviceId, Identity.UserId, rosterSeq, Capabilities.None, []);

    /// <summary>Sends one message; AMP/1's states decide what may be sent when.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels sending.</param>
    /// <returns>A task that completes when it was written.</returns>
    public async Task SendAsync(MeshMessage message, CancellationToken cancellationToken)
    {
        Session<MeshMessage, MeshMessage> open = await SessionAsync(cancellationToken).ConfigureAwait(false);
        await open.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the next message the node sent, which must be a <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The message.</returns>
    /// <exception cref="TestMeshException">Another message came, the session ended, or nothing came within the deadline.</exception>
    public async Task<T> ExpectAsync<T>(CancellationToken cancellationToken)
        where T : MeshMessage
    {
        Session<MeshMessage, MeshMessage> open = await SessionAsync(cancellationToken).ConfigureAwait(false);
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        ProtocolReadResult<MeshMessage> read;
        try
        {
            read = await open.ReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Unexpected(typeof(T).Name, $"nothing within {TestMesh.Deadline.TotalSeconds:0} s");
        }

        return read.Message as T ?? throw Unexpected(typeof(T).Name, read.Message?.ToString() ?? "the end of the session");
    }

    /// <summary>
    /// Writes a message's frame straight to the TLS stream, outside AMP/1's states and without a session of this side:
    /// no heartbeat and no automatic answer follow, so the node receives exactly this and then nothing.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels writing.</param>
    /// <returns>A task that completes when the frame was written.</returns>
    public async Task SendRawAsync(MeshMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (session is not null)
            throw new InvalidOperationException("The session writes on this stream; send raw frames before anything is sent or read through it.");
        var frame = new ArrayBufferWriter<byte>();
        using (var codec = new CodecRunner<MeshMessage, MeshMessage>(Amp.Definition))
            codec.Encode(message, frame);
        await tls.WriteAsync(frame.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes only the first <paramref name="length"/> bytes of a message's frame straight to the TLS stream, so the node
    /// sees a frame begin and not end (SPEC.md §5.1). Allowed with a session open, while it writes nothing itself: nothing
    /// is sent automatically before 60 seconds of silence.
    /// </summary>
    /// <param name="message">The message whose frame is begun.</param>
    /// <param name="length">How many of its bytes to write; fewer than the whole frame.</param>
    /// <param name="cancellationToken">Cancels writing.</param>
    /// <returns>A task that completes when the bytes were written.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The length is not below the frame's.</exception>
    public async Task SendFrameStartAsync(MeshMessage message, int length, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var frame = new ArrayBufferWriter<byte>();
        using (var codec = new CodecRunner<MeshMessage, MeshMessage>(Amp.Definition))
            codec.Encode(message, frame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(length, frame.WrittenCount);
        await tls.WriteAsync(frame.WrittenMemory[..length], cancellationToken).ConfigureAwait(false);
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads until the node sends <c>Bye</c>, passing over what comes before it.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The node's <c>Bye</c>.</returns>
    /// <exception cref="TestMeshException">The session ended without a <c>Bye</c>, or none came within the deadline.</exception>
    public async Task<Bye> ExpectByeAsync(CancellationToken cancellationToken)
    {
        Session<MeshMessage, MeshMessage> open = await SessionAsync(cancellationToken).ConfigureAwait(false);
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            while (true)
            {
                ProtocolReadResult<MeshMessage> read = await open.ReadAsync(linked.Token).ConfigureAwait(false);
                if (read.Message is Bye bye)
                    return bye;
                if (read.IsCompleted)
                    throw Unexpected("Bye", "the end of the session");
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Unexpected("Bye", $"none within {TestMesh.Deadline.TotalSeconds:0} s");
        }
    }

    /// <summary>Reads until the node ends the session; what it sent on the way is ignored.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when the session ended.</returns>
    /// <exception cref="TestMeshException">The session did not end within the deadline.</exception>
    public async Task ExpectClosedAsync(CancellationToken cancellationToken)
    {
        Session<MeshMessage, MeshMessage> open = await SessionAsync(cancellationToken).ConfigureAwait(false);
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            while (!(await open.ReadAsync(linked.Token).ConfigureAwait(false)).IsCompleted)
            {
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Unexpected("the end of the session", $"none within {TestMesh.Deadline.TotalSeconds:0} s");
        }
        catch (Exception ex) when (ex is ProtoStream.Errors.ProtoStreamException or IOException or AuthenticationException)
        {
            // Ended by a reset or a refused handshake: ended all the same.
        }
    }

    /// <summary>
    /// Expects the node to have closed without a single protocol byte (SPEC.md §4.3): the handshake failed, or the TLS
    /// stream ends (or is reset) before any byte arrives. Call it before anything else is read.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when the node closed.</returns>
    /// <exception cref="TestMeshException">A byte arrived, or the connection stayed open past the deadline.</exception>
    public async Task ExpectClosedWithoutHelloAsync(CancellationToken cancellationToken)
    {
        if (HandshakeFailure is not null)
            return;
        if (session is not null)
            throw new InvalidOperationException("The session already read from the stream; expect the close before anything else.");

        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        byte[] buffer = new byte[64];
        int read;
        try
        {
            read = await tls.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Unexpected("a close without a protocol byte", $"an open connection after {TestMesh.Deadline.TotalSeconds:0} s");
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException or ObjectDisposedException)
        {
            return;
        }

        if (read > 0)
            throw Unexpected("a close without a protocol byte", $"{read} bytes of protocol: {Convert.ToHexString(buffer, 0, read)}");
    }

    /// <summary>
    /// Sends a frame of <c>axiom-mesh-pair/1</c> (PAIR-SPEC.md §5): the connection then speaks the pairing protocol, never
    /// AMP/1. For a connection whose handshake negotiated the pairing ALPN.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it was written.</returns>
    public async Task SendPairAsync(PairMessage message, CancellationToken cancellationToken)
    {
        Session<PairMessage, PairMessage> open = await PairingAsync(cancellationToken).ConfigureAwait(false);
        await open.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the next <c>axiom-mesh-pair/1</c> message the node sends; a frame of any other protocol (AMP/1 among them) does
    /// not parse and fails the test.
    /// </summary>
    /// <typeparam name="T">The message expected.</typeparam>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The message.</returns>
    /// <exception cref="TestMeshException">Something else arrived, or nothing within the deadline.</exception>
    public async Task<T> ExpectPairAsync<T>(CancellationToken cancellationToken)
        where T : PairMessage
    {
        Session<PairMessage, PairMessage> open = await PairingAsync(cancellationToken).ConfigureAwait(false);
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        ProtocolReadResult<PairMessage> read;
        try
        {
            read = await open.ReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Unexpected(typeof(T).Name, $"nothing within {TestMesh.Deadline.TotalSeconds:0} s");
        }

        return read.Message as T ?? throw Unexpected(typeof(T).Name, read.Message?.ToString() ?? "the end of the session");
    }

    /// <summary>
    /// Waits until the node closes the pairing connection, and fails when any frame arrives before the close: the node
    /// sends nothing after a pairing ended, of AMP/1 or of the pairing protocol.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when the connection closed.</returns>
    /// <exception cref="TestMeshException">A frame arrived, or the connection stayed open past the deadline.</exception>
    public async Task ExpectPairingClosedAsync(CancellationToken cancellationToken)
    {
        Session<PairMessage, PairMessage> open = await PairingAsync(cancellationToken).ConfigureAwait(false);
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        ProtocolReadResult<PairMessage> read;
        try
        {
            read = await open.ReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Unexpected("the end of the pairing connection", $"an open connection after {TestMesh.Deadline.TotalSeconds:0} s");
        }
        catch (ProtoStream.Errors.ProtocolViolationException violation)
        {
            throw Unexpected("the end of the pairing connection", "a frame that is not of the pairing protocol: " + violation.Violation.Detail);
        }
        catch (Exception ex) when (ex is ProtoStream.Errors.ProtoStreamException or IOException or AuthenticationException)
        {
            // Ended by a reset: ended all the same.
            return;
        }

        if (!read.IsCompleted)
            throw Unexpected("the end of the pairing connection", read.Message?.ToString() ?? "a message");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
            await connection.DisposeAsync().ConfigureAwait(false);
        await tls.DisposeAsync().ConfigureAwait(false);
        certificate.Dispose();
    }

    private async ValueTask<Session<MeshMessage, MeshMessage>> SessionAsync(CancellationToken cancellationToken)
    {
        if (session is not null)
            return session;
        if (pairing is not null)
            throw new InvalidOperationException("The connection speaks axiom-mesh-pair/1 already.");
        if (HandshakeFailure is not null)
            throw Unexpected("a completed handshake", HandshakeFailure.Message);
        connection = Connection.FromStream(tls, new ConnectionOptions { TimeProvider = clock, LeaveOpen = true });
        session = await connection.OpenAsync(Amp.Definition, cancellationToken).ConfigureAwait(false);
        return session;
    }

    private async ValueTask<Session<PairMessage, PairMessage>> PairingAsync(CancellationToken cancellationToken)
    {
        if (pairing is not null)
            return pairing;
        if (session is not null)
            throw new InvalidOperationException("The connection speaks AMP/1 already; a pairing connection speaks only axiom-mesh-pair/1.");
        if (HandshakeFailure is not null)
            throw Unexpected("a completed handshake", HandshakeFailure.Message);
        connection = Connection.FromStream(tls, new ConnectionOptions { TimeProvider = clock, LeaveOpen = true });
        pairing = await connection.OpenAsync(PairProtocol.Definition, cancellationToken).ConfigureAwait(false);
        return pairing;
    }

    private static X509Certificate2 CertificateOf(TestIdentity identity, TimeProvider clock)
    {
        using ECDsa key = ECDsa.Create();
        byte[] pkcs8 = identity.DevicePkcs8();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out _);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        return DeviceCertificate.Create(key, clock.GetUtcNow());
    }

    private bool Remember(object sender, X509Certificate? presented, X509Chain? chain, SslPolicyErrors errors)
    {
        if (presented is not null && DeviceCertificate.TryGetPoint(presented, out ReadOnlyMemory<byte> point))
            RemoteDevice = new DeviceId(Hash32.Of(point.Span));
        return true;
    }

    private TestMeshException Unexpected(string expected, string got) => new(
        $"The scripted peer {Identity.Name} (ALPN {NegotiatedAlpn ?? "none"}) expected {expected} from the node, and got {got}.",
        "Read the node's facts (MeshNode.Facts): they say what the node decided about this peer and why.");
}
