using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Integration.Tests.Rules;

/// <summary>
/// SPEC.md §4.2 on a real socket, the core of what tls-scan.yml checks with testssl.sh: a real node negotiates TLS 1.3 with
/// a forward-secret AEAD suite and the ALPN <c>axiom-mesh/1</c>, refuses a client that offers nothing newer than TLS 1.2,
/// closes a connection that negotiated no ALPN without sending a protocol byte, and on Linux (OpenSSL) issues no session
/// ticket.
/// </summary>
public sealed class TlsConformanceTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SslApplicationProtocol Mesh = new("axiom-mesh/1");

    // §4.2: no session ticket. On OpenSSL .NET's AllowTlsResume = false sets SSL_OP_NO_TICKET alone, which in TLS 1.3 means
    // a stateful ticket, not none (OpenSslTickets in Core says where); the mesh port sets the ticket count to zero. Seen from
    // the client: the record that carries the node's first application byte (its Hello) is the first record after the
    // handshake, with no NewSessionTicket before it. SChannel is not covered: Windows is not where tls-scan.yml runs.
    [OsFact("linux")]
    public async Task OnLinuxTheMeshPortIssuesNoSessionTicket()
    {
        await using RealMeshFixture mesh = await TrustingBobAsync();
        using X509Certificate2 bob = CertificateOf(TestIdentity.Of("bob-desktop"));
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        TlsRecordFeed? feed = null;

        await using SslStream tls = await HandshakeAsync(mesh, bob, SslProtocols.Tls13, [Mesh], connection => feed = new TlsRecordFeed(connection));
        int afterHandshake = feed!.Records;
        int read = await tls.ReadAsync(new byte[64], deadline.Token);

        Assert.True(read > 0, "The node sent no Hello.");
        Assert.Equal(1, feed.Records - afterHandshake);
    }

    // The runtime gap the mesh port closes, pinned so its closing is noticed: a bare SslStream server on OpenSSL with
    // AllowTlsResume = false still sends OpenSSL's default two NewSessionTickets before its first application byte, and the
    // record count above sees them. When this fails on a newer runtime, OpenSslTickets can go.
    [OsFact("linux")]
    public async Task OnLinuxTheRecordCountSeesTheTwoTicketsABareSslStreamServerStillIssues()
    {
        using X509Certificate2 alice = CertificateOf(TestIdentity.Of("alice-laptop"));
        using X509Certificate2 bob = CertificateOf(TestIdentity.Of("bob-desktop"));
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Task server = ServeOneByteAsync(listener, alice, deadline.Token);

        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, deadline.Token);
        var feed = new TlsRecordFeed(client.GetStream());
        await using var tls = new SslStream(feed, leaveInnerStreamOpen: false);
        await tls.AuthenticateAsClientAsync(ClientOptions(bob, SslProtocols.Tls13, [Mesh]), deadline.Token);
        int afterHandshake = feed.Records;
        int read = await tls.ReadAsync(new byte[64], deadline.Token);
        await server;

        Assert.Equal(1, read);
        Assert.Equal(3, feed.Records - afterHandshake);
    }

    // §4.2: TLS 1.3, an AEAD suite (TLS_AES_128_GCM_SHA256 required, the other two recommended), ALPN negotiated.
    [Fact]
    public async Task ARealNodeNegotiatesTls13WithAnAeadSuiteAndTheMeshAlpn()
    {
        await using RealMeshFixture mesh = await TrustingBobAsync();
        using X509Certificate2 bob = CertificateOf(TestIdentity.Of("bob-desktop"));

        await using SslStream tls = await HandshakeAsync(mesh, bob, SslProtocols.Tls13, [Mesh]);

        Assert.Equal(SslProtocols.Tls13, tls.SslProtocol);
        Assert.Contains(tls.NegotiatedCipherSuite, new[] { TlsCipherSuite.TLS_AES_128_GCM_SHA256, TlsCipherSuite.TLS_AES_256_GCM_SHA384, TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256 });
        Assert.Equal(Mesh, tls.NegotiatedApplicationProtocol);
    }

    // §4.2: older versions are neither offered nor accepted; a client limited to TLS 1.2 gets no handshake.
    [Fact]
    public async Task AClientThatOffersOnlyTls12IsRefused()
    {
        await using RealMeshFixture mesh = await TrustingBobAsync();
        using X509Certificate2 bob = CertificateOf(TestIdentity.Of("bob-desktop"));

        Exception refused = await Assert.ThrowsAnyAsync<Exception>(async () => await (await HandshakeAsync(mesh, bob, SslProtocols.Tls12, [Mesh])).DisposeAsync());

        Assert.True(refused is AuthenticationException or IOException, refused.ToString());
    }

    // §4.2, §4.3: a connection without a negotiated ALPN is closed, and without a protocol byte.
    [Fact]
    public async Task AConnectionWithoutTheAlpnIsClosedWithoutAProtocolByte()
    {
        await using RealMeshFixture mesh = await TrustingBobAsync();
        using X509Certificate2 bob = CertificateOf(TestIdentity.Of("bob-desktop"));
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        int read;

        try
        {
            await using SslStream tls = await HandshakeAsync(mesh, bob, SslProtocols.Tls13, null);
            read = await tls.ReadAsync(new byte[16], deadline.Token);
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException)
        {
            read = 0; // closed during or right after the handshake
        }

        Assert.Equal(0, read);
    }

    // Alice's laptop alone on loopback, knowing Bob, so Bob's desktop passes the peer check of §4.3.
    private static async Task<RealMeshFixture> TrustingBobAsync()
    {
        RealMeshFixture mesh = await RealMeshFixture.StartAsync(["alice-laptop"], [], [], [], None);
        await mesh.Node("alice-laptop").AcceptUserAsync(TestIdentity.RosterOf("bob"), None);
        return mesh;
    }

    private static async Task<SslStream> HandshakeAsync(RealMeshFixture mesh, X509Certificate2 certificate, SslProtocols protocols, List<SslApplicationProtocol>? alpn, Func<Stream, Stream>? under = null)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(mesh.Node("alice-laptop").MeshEndpoint, deadline.Token);
        Stream connection = client.GetStream();
        var tls = new SslStream(under is null ? connection : under(connection), leaveInnerStreamOpen: false);
        try
        {
            await tls.AuthenticateAsClientAsync(ClientOptions(certificate, protocols, alpn), deadline.Token);
            return tls;
        }
        catch
        {
            await tls.DisposeAsync();
            throw;
        }
    }

    private static SslClientAuthenticationOptions ClientOptions(X509Certificate2 certificate, SslProtocols protocols, List<SslApplicationProtocol>? alpn) => new()
    {
        TargetHost = "axiom-mesh",
        ClientCertificateContext = SslStreamCertificateContext.Create(certificate, null, offline: true),
        EnabledSslProtocols = protocols,
        ApplicationProtocols = alpn,
        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        RemoteCertificateValidationCallback = static (_, _, _, _) => true,
    };

    // A server as .NET gives it, with the options the mesh port sets short of the ticket count: TLS 1.3, a client
    // certificate, no resumption. It writes one byte after the handshake and closes.
    private static async Task ServeOneByteAsync(TcpListener listener, X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        using TcpClient accepted = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var tls = new SslStream(accepted.GetStream(), leaveInnerStreamOpen: false);
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificateContext = SslStreamCertificateContext.Create(certificate, null, offline: true),
            ClientCertificateRequired = true,
            EnabledSslProtocols = SslProtocols.Tls13,
            ApplicationProtocols = [Mesh],
            AllowRenegotiation = false,
            AllowTlsResume = false,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            RemoteCertificateValidationCallback = static (_, _, _, _) => true,
        }, cancellationToken);
        await tls.WriteAsync(new byte[] { 1 }, cancellationToken);
        await tls.FlushAsync(cancellationToken);
    }

    private static X509Certificate2 CertificateOf(TestIdentity identity)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(identity.DevicePkcs8(), out _);
        return DeviceCertificate.Create(key, DateTimeOffset.UtcNow);
    }
}
