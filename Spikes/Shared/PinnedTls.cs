using System;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE.Connections;
using Axiom.LWSE.Transports;

namespace Axiom.Mesh.Spikes;

public enum TlsRole
{
    Unknown = 0,
    Server,
    Client,
}

/// <summary>What a port states for the pinned TLS layer: its own certificate, which side of the handshake it runs, and whom it accepts.</summary>
public sealed class PinnedTlsOptions
{
    public required X509Certificate2 Certificate { get; init; }

    public required TlsRole Role { get; init; }

    /// <summary>Accepts a peer by the SubjectPublicKeyInfo of its certificate; nothing else about the certificate counts.</summary>
    public required Func<ReadOnlyMemory<byte>, bool> IsPinned { get; init; }
}

/// <summary>What the layer learned about the peer, attached to the connection.</summary>
public sealed record PeerIdentity(string PublicKeyFingerprint, SslProtocols Protocol, TlsCipherSuite CipherSuite, string? Alpn)
{
    public override string ToString() => $"{Protocol} {CipherSuite} alpn={Alpn ?? "-"} peer={PublicKeyFingerprint}";
}

/// <summary>
/// Mutual TLS 1.3 pinned to public keys: no CA, no names. The same layer runs as server on an inbound port and as
/// client on the port that serves adopted outbound connections; which one is a port option.
/// </summary>
public sealed class PinnedTlsLayer(PinnedTlsOptions options) : ITransportLayer
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly SslApplicationProtocol Alpn = new(HelloProtocol.Alpn);

    public async ValueTask<TransportResult> WrapAsync(Stream stream, ConnectionContext connection, CancellationToken cancellationToken)
    {
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false, Validate);
        try
        {
            using ConnectionDeadline deadline = connection.ArmDeadline(HandshakeTimeout);
            if (options.Role == TlsRole.Server)
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = options.Certificate,
                    ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.Tls13,
                    ApplicationProtocols = [Alpn],
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    AllowRenegotiation = false,
                }, deadline.Token).ConfigureAwait(false);
            }
            else
            {
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "axiom-mesh",
                    ClientCertificates = [options.Certificate],
                    EnabledSslProtocols = SslProtocols.Tls13,
                    ApplicationProtocols = [Alpn],
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    AllowRenegotiation = false,
                }, deadline.Token).ConfigureAwait(false);
            }

            if (ssl.RemoteCertificate is null)
                throw new AuthenticationException("The peer sent no certificate.");
            if (!ssl.IsMutuallyAuthenticated)
                throw new AuthenticationException("The handshake is not mutually authenticated.");
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        string? alpn = ssl.NegotiatedApplicationProtocol == default ? null : ssl.NegotiatedApplicationProtocol.ToString();
        connection.SetFeature(new PeerIdentity(Certificates.Fingerprint(ssl.RemoteCertificate), ssl.SslProtocol, ssl.NegotiatedCipherSuite, alpn));
        return new TransportResult(ssl);
    }

    private bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors) =>
        certificate is not null && options.IsPinned(Certificates.SubjectPublicKeyInfo(certificate));
}
