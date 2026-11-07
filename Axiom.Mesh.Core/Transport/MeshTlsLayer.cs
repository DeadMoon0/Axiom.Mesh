using System;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE.Connections;
using Axiom.LWSE.Transports;

namespace Axiom.Mesh.Core;

/// <summary>
/// Mutual TLS 1.3 pinned to the rosters (SPEC.md §4.2, §4.3; DESIGN.md §6.3), on the mesh port as server and on the
/// outbound port as client. Every connection is a fresh <see cref="SslStream"/> with a full handshake: no session
/// tickets, no resumption, no early data, no renegotiation, no revocation or chain check. The handshake must complete
/// within 10 seconds on the node's clock. A peer that fails any check is closed without a protocol byte; one that
/// passes gets a <see cref="PeerIdentity"/> and a <see cref="LinkActivity"/> as connection features.
/// </summary>
/// <remarks>One instance serves every connection of its port at once; all per-connection state lives in a <see cref="PeerCheck"/>.</remarks>
internal sealed class MeshTlsLayer : ITransportLayer
{
    private static readonly TimeSpan Handshake = TimeSpan.FromSeconds(Limits.TlsHandshakeSeconds);
    private readonly MeshTlsOptions options;

    /// <summary>The layer of one port.</summary>
    /// <param name="options">The port's options.</param>
    public MeshTlsLayer(MeshTlsOptions options) => this.options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async ValueTask<TransportResult> WrapAsync(Stream stream, ConnectionContext connection, CancellationToken cancellationToken)
    {
        DeviceCredential credential = options.Credential()
            ?? throw new AuthenticationException("This device has no device key yet, so it has no certificate to present.");
        var check = new PeerCheck(options, options.Pairing.Current, connection.Remote);
        SslStream tls = options.Role == TlsRole.Server && OpenSslTickets.Applies ? ServerStreamIssuingNoTicket(stream) : new SslStream(stream, leaveInnerStreamOpen: false);
        try
        {
            using (ConnectionDeadline deadline = connection.ArmDeadline(Handshake))
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
                try
                {
                    if (options.Role == TlsRole.Server)
                        await tls.AuthenticateAsServerAsync(ServerOptions(credential, options.Pairing.Current.IsShowing, check.Validate), linked.Token).ConfigureAwait(false);
                    else
                        await tls.AuthenticateAsClientAsync(ClientOptions(credential, check.IsEnteringDial, check.Validate), linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (deadline.HasExpired)
                {
                    throw new TimeoutException($"The TLS handshake did not complete within {Limits.TlsHandshakeSeconds} seconds (SPEC.md §4.2).");
                }
            }

            PeerIdentity identity = check.Decide(tls, options.Pairing.Current, out string refusal)
                ?? throw new AuthenticationException($"The peer is refused after the handshake: {refusal} (SPEC.md §4.3).");
            var activity = new LinkActivity(options.Time, tracksFrames: !identity.IsPairing);
            connection.SetFeature(identity);
            connection.SetFeature(activity);
            return new TransportResult(new ActivityStream(tls, activity));
        }
        catch
        {
            await tls.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The server side's handshake options: TLS 1.3 only, a client certificate required, no resumption.</summary>
    /// <param name="credential">What this device presents.</param>
    /// <param name="offerPairing">Whether a pairing waits, so the pairing ALPN is offered too.</param>
    /// <param name="validate">The certificate callback of this handshake.</param>
    /// <returns>New options for one handshake.</returns>
    public static SslServerAuthenticationOptions ServerOptions(DeviceCredential credential, bool offerPairing, RemoteCertificateValidationCallback validate) => new()
    {
        ServerCertificateContext = credential.Context,
        ClientCertificateRequired = true,
        EnabledSslProtocols = SslProtocols.Tls13,
        ApplicationProtocols = offerPairing ? MeshTls.MeshAndPairing() : MeshTls.MeshOnly(),
        AllowRenegotiation = false,
        AllowTlsResume = false,
        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        CertificateChainPolicy = NoChainChecks(),
        RemoteCertificateValidationCallback = validate,
    };

    /// <summary>The client side's handshake options: TLS 1.3 only, the device certificate presented, no resumption.</summary>
    /// <param name="credential">What this device presents.</param>
    /// <param name="pairing">Whether this is the entering side's pairing dial, which offers only the pairing ALPN.</param>
    /// <param name="validate">The certificate callback of this handshake.</param>
    /// <returns>New options for one handshake.</returns>
    public static SslClientAuthenticationOptions ClientOptions(DeviceCredential credential, bool pairing, RemoteCertificateValidationCallback validate) => new()
    {
        TargetHost = MeshTls.TargetHost,
        ClientCertificateContext = credential.Context,
        EnabledSslProtocols = SslProtocols.Tls13,
        ApplicationProtocols = pairing ? MeshTls.PairingOnly() : MeshTls.MeshOnly(),
        AllowRenegotiation = false,
        AllowTlsResume = false,
        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        CertificateChainPolicy = NoChainChecks(),
        RemoteCertificateValidationCallback = validate,
    };

    // SPEC.md §4.2, no session ticket: on OpenSSL the server's ticket count is set to zero while it writes its first flight,
    // the only moment between the handle's creation and the client's Finished (OpenSslTickets says why).
    private static SslStream ServerStreamIssuingNoTicket(Stream stream)
    {
        var hook = new FirstWriteHook(stream);
        var tls = new SslStream(hook, leaveInnerStreamOpen: false);
        hook.Arm(() => OpenSslTickets.IssueNone(tls));
        return tls;
    }

    // The key is pinned, so no chain counts (SPEC.md §4.3): build none against the system's stores, fetch nothing.
    private static X509ChainPolicy NoChainChecks() => new()
    {
        RevocationMode = X509RevocationMode.NoCheck,
        TrustMode = X509ChainTrustMode.CustomRootTrust,
        DisableCertificateDownloads = true,
    };
}
