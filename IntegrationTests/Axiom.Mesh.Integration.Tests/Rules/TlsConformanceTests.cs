using System;
using System.IO;
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
/// and closes a connection that negotiated no ALPN without sending a protocol byte.
/// </summary>
public sealed class TlsConformanceTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SslApplicationProtocol Mesh = new("axiom-mesh/1");

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

    private static async Task<SslStream> HandshakeAsync(RealMeshFixture mesh, X509Certificate2 certificate, SslProtocols protocols, System.Collections.Generic.List<SslApplicationProtocol>? alpn)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(mesh.Node("alice-laptop").MeshEndpoint, deadline.Token);
        var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        try
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "axiom-mesh",
                ClientCertificateContext = SslStreamCertificateContext.Create(certificate, null, offline: true),
                EnabledSslProtocols = protocols,
                ApplicationProtocols = alpn,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
            }, deadline.Token);
            return tls;
        }
        catch
        {
            await tls.DisposeAsync();
            throw;
        }
    }

    private static X509Certificate2 CertificateOf(TestIdentity identity)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(identity.DevicePkcs8(), out _);
        return DeviceCertificate.Create(key, DateTimeOffset.UtcNow);
    }
}
