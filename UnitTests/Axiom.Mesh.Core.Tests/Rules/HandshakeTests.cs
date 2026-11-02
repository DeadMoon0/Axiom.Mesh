using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// SPEC.md §4.2 and §4.3: every connection is mutual TLS 1.3 with a negotiated ALPN, completed within 10 seconds, and a
/// peer whose key is not a device of a known user's current Roster is closed without a single protocol byte.
/// </summary>
public sealed class HandshakeTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §4.3: Eve's key is in no Roster Alice holds; the node closes before AMP/1 begins and opens no session for her.
    [Fact]
    public async Task APeerWhoseKeyIsInNoRosterIsClosedWithoutAProtocolByte()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        MeshNode alice = mesh["alice-laptop"].Node;

        await using ScriptedPeer eve = await ScriptedPeer.ConnectAsync(mesh["alice-laptop"], TestIdentity.Of("eve-device"), ScriptedPeerOptions.Default, None);

        await eve.ExpectClosedWithoutHelloAsync(None);
        await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.HandshakeFailed, 0, "Eve's handshake refused", None);
        Assert.DoesNotContain(alice.Facts.Recent(), f => f.Kind == NodeFactKind.SessionOpened);
        Assert.DoesNotContain(alice.Peers, p => p.Device == TestIdentity.Of("eve-device").DeviceId);
    }

    // §4.3, §8.8: once Bob's Roster removes his desktop, its key is refused at the handshake like a stranger's.
    [Fact]
    public async Task ARemovedDeviceIsRefusedAtTheHandshake()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        await mesh["alice-laptop"].Node.AcceptUserAsync(TestIdentity.RosterOf("bob", 2, "bob-desktop"), None);

        await using ScriptedPeer desktop = await ScriptedPeer.ConnectAsync(mesh["alice-laptop"], TestIdentity.Of("bob-desktop"), ScriptedPeerOptions.Default, None);

        await desktop.ExpectClosedWithoutHelloAsync(None);
        Assert.DoesNotContain(mesh["alice-laptop"].Node.Facts.Recent(), f => f.Kind == NodeFactKind.SessionOpened);
    }

    // §4.2: no client certificate, or no ALPN, fails the connection without a protocol byte; a handshake that does not
    // complete is closed when 10 seconds have passed on the node's clock, and not before. The certificate-less case is
    // decided where the node decides it, in the certificate callback: on Windows an in-process client given no
    // certificate presents one an earlier client of the process used, so it cannot be scripted end to end.
    [Fact]
    public async Task AHandshakeWithoutAClientCertificateOrWithoutAlpnFails_AndCompletesWithin10Seconds()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode alice = mesh["alice-laptop"];
        TestIdentity bob = TestIdentity.Of("bob-desktop");

        var options = new MeshTlsOptions { Role = TlsRole.Server, Credential = () => null, Trust = () => alice.Node.Trust, Pairing = new PairingGate(), Time = mesh.Clock };
        var check = new PeerCheck(options, PairingWindow.Showing, new IPEndPoint(IPAddress.Parse("10.0.0.200"), 50000));
        Assert.False(check.Validate(new object(), null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
        using DeviceCredential credential = DeviceCredential.Create(TestIdentity.Of("alice-laptop").DevicePkcs8(), TestIdentity.Epoch);
        Assert.True(MeshTlsLayer.ServerOptions(credential, offerPairing: false, check.Validate).ClientCertificateRequired);

        await using ScriptedPeer withoutAlpn = await ScriptedPeer.ConnectAsync(alice, bob, ScriptedPeerOptions.Default with { Alpn = [], From = new IPEndPoint(IPAddress.Parse("10.0.0.201"), 50000) }, None);
        await withoutAlpn.ExpectClosedWithoutHelloAsync(None);
        Assert.DoesNotContain(alice.Node.Facts.Recent(), f => f.Kind == NodeFactKind.SessionOpened);

        // A client that sends its ClientHello and then stalls: the node answers, then waits for the rest.
        await using var stall = new StallingClient(alice.Node.ConnectInMemory(new IPEndPoint(IPAddress.Parse("10.0.0.202"), 50000)));
        await stall.StartAsync();
        long before = alice.Node.Facts.LastSequence;
        mesh.Clock.Advance(TimeSpan.FromSeconds(9));
        Assert.DoesNotContain(alice.Node.Facts.Recent(), f => f.Sequence > before && f.Kind == NodeFactKind.HandshakeFailed);

        mesh.Clock.Advance(TimeSpan.FromSeconds(1));
        NodeFact failed = await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.HandshakeFailed, before, "the stalled handshake closed", None);
        Assert.Contains("10 seconds", failed.Detail, StringComparison.Ordinal);
    }

    // PAIR-SPEC §2.4, §4.2: the entering side's certificate callback admits only a key whose hash is the pinned device id,
    // whether the device was named by a code's key or by the id discovery announced, and only on the dial it pinned.
    [Fact]
    public async Task TheEnteringDialAdmitsOnlyACertificateWhoseKeyHashesToThePinnedDeviceId()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode alice = mesh["alice-laptop"];
        var options = new MeshTlsOptions { Role = TlsRole.Client, Credential = () => null, Trust = () => alice.Node.Trust, Pairing = new PairingGate(), Time = mesh.Clock };
        var dialled = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 41810);
        var other = new IPEndPoint(IPAddress.Parse("10.0.0.3"), 41810);
        using DeviceCredential bob = DeviceCredential.Create(TestIdentity.Of("bob-desktop").DevicePkcs8(), TestIdentity.Epoch);
        using DeviceCredential eve = DeviceCredential.Create(TestIdentity.Of("eve-device").DevicePkcs8(), TestIdentity.Epoch);
        PairingAddress[] addresses = [new PairingAddress("10.0.0.2", 41810)];
        PairingWindow byId = PairingWindow.Entering(PairingTarget.OfDevice(bob.Device, addresses), dialled);
        PairingWindow byCode = PairingWindow.Entering(PairingTarget.OfCode(new PairingCode(Protocol.PairingMode.ShowUser, addresses, bob.Key)), dialled);

        foreach (PairingWindow window in new[] { byId, byCode })
        {
            Assert.True(new PeerCheck(options, window, dialled).IsEnteringDial);
            Assert.True(new PeerCheck(options, window, dialled).Validate(new object(), bob.Certificate, null, SslPolicyErrors.None));
            Assert.False(new PeerCheck(options, window, dialled).Validate(new object(), eve.Certificate, null, SslPolicyErrors.None));
            Assert.False(new PeerCheck(options, window, dialled).Validate(new object(), null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
            Assert.False(new PeerCheck(options, window, other).IsEnteringDial);
            Assert.False(new PeerCheck(options, window, other).Validate(new object(), bob.Certificate, null, SslPolicyErrors.None));
        }

        Assert.False(new PeerCheck(options, PairingWindow.Closed, dialled).Validate(new object(), bob.Certificate, null, SslPolicyErrors.None));
    }

    // §4.2, §13: no session tickets and no resumption, so every connection is a full handshake judged against the
    // rosters current at that moment; TLS 1.3 only, a client certificate required, no renegotiation. SslStream offers no
    // early data at all, so with resumption off none can be accepted.
    [Fact]
    public void NoSessionTicketsAndNoEarlyData()
    {
        using DeviceCredential credential = DeviceCredential.Create(TestIdentity.Of("alice-laptop").DevicePkcs8(), TestIdentity.Epoch);
        RemoteCertificateValidationCallback accept = (_, _, _, _) => true;

        SslServerAuthenticationOptions server = MeshTlsLayer.ServerOptions(credential, offerPairing: false, accept);
        SslClientAuthenticationOptions client = MeshTlsLayer.ClientOptions(credential, pairing: false, accept);

        Assert.False(server.AllowTlsResume);
        Assert.False(client.AllowTlsResume);
        Assert.Equal(SslProtocols.Tls13, server.EnabledSslProtocols);
        Assert.Equal(SslProtocols.Tls13, client.EnabledSslProtocols);
        Assert.True(server.ClientCertificateRequired);
        Assert.False(server.AllowRenegotiation);
        Assert.False(client.AllowRenegotiation);
        Assert.Equal([Amp.Alpn], server.ApplicationProtocols!.Select(p => p.ToString()));
        Assert.Equal([Amp.Alpn], client.ApplicationProtocols!.Select(p => p.ToString()));
    }

    // PAIR-SPEC §4.1, §4.2: while a pairing waits the listener offers the pairing ALPN too, and lets an unknown key through
    // under it only; under AMP/1's ALPN the unknown key is refused as ever, before a protocol byte. Without a pairing the
    // pairing ALPN admits nobody.
    [Fact]
    public async Task UnderTheMeshAlpnAnUnknownKeyIsRefusedWhileAPairingWaits_UnderThePairingAlpnItIsLetThrough()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode alice = mesh["alice-laptop"];
        TestIdentity eve = TestIdentity.Of("eve-device");
        ScriptedPeerOptions pairing = ScriptedPeerOptions.Default with { Alpn = [PairProtocol.Alpn], From = new IPEndPoint(IPAddress.Parse("10.0.0.201"), 50000) };

        await using (ScriptedPeer withoutWindow = await ScriptedPeer.ConnectAsync(alice, eve, pairing, None))
            await withoutWindow.ExpectClosedWithoutHelloAsync(None);

        alice.Node.PairingGate.Set(PairingWindow.Showing);
        await using (ScriptedPeer underMesh = await ScriptedPeer.ConnectAsync(alice, eve, ScriptedPeerOptions.Default, None))
            await underMesh.ExpectClosedWithoutHelloAsync(None);
        Assert.DoesNotContain(alice.Node.Facts.Recent(), f => f.Kind == NodeFactKind.SessionOpened);

        long before = alice.Node.Facts.LastSequence;
        await using ScriptedPeer underPairing = await ScriptedPeer.ConnectAsync(alice, eve, pairing with { From = new IPEndPoint(IPAddress.Parse("10.0.0.202"), 50000) }, None);
        Assert.True(underPairing.HandshakeCompleted, underPairing.HandshakeFailure?.ToString());
        Assert.Equal(PairProtocol.Alpn, underPairing.NegotiatedAlpn);
        await underPairing.ExpectClosedWithoutHelloAsync(None);
        NodeFact closed = await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.Closed, before, "the pairing connection closed", None);
        Assert.DoesNotContain(alice.Node.Facts.Recent(), f => f.Kind == NodeFactKind.HandshakeFailed && f.Connection == closed.Connection);
    }

    /// <summary>A TLS client that sends its first flight, reads the server's answer, and then sends nothing more.</summary>
    private sealed class StallingClient(Stream stream) : IAsyncDisposable
    {
        private readonly Gate gate = new(stream);
        private Task? handshake;

        public async Task StartAsync()
        {
            var tls = new SslStream(gate, leaveInnerStreamOpen: true, (_, _, _, _) => true);
            handshake = tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "axiom-mesh",
                EnabledSslProtocols = SslProtocols.Tls13,
                ApplicationProtocols = [new SslApplicationProtocol(Amp.Alpn)],
            });
            await gate.Answered.Task.WaitAsync(TestMesh.Deadline);
        }

        public async ValueTask DisposeAsync()
        {
            gate.Release();
            await stream.DisposeAsync();
            if (handshake is not null)
                await Task.WhenAny(handshake);
        }

        /// <summary>Passes the first write and every read; holds every later write until released.</summary>
        private sealed class Gate(Stream inner) : Stream
        {
            private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int writes;

            public TaskCompletionSource Answered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public override bool CanRead => true;

            public override bool CanWrite => true;

            public override bool CanSeek => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public void Release() => released.TrySetResult();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                int read = await inner.ReadAsync(buffer, cancellationToken);
                if (read > 0)
                    Answered.TrySetResult();
                return read;
            }

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref writes) > 1)
                {
                    await released.Task;
                    throw new IOException("The stalling client never sends its second flight.");
                }

                await inner.WriteAsync(buffer, cancellationToken);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override void Flush()
            {
            }

            public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
