using System;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core;

/// <summary>
/// The peer validation of one handshake (SPEC.md §4.3, PAIR-SPEC.md §4.2), in two steps. Inside the handshake, the
/// certificate callback accepts only a key that resolves to a not-removed device of a known user's current Roster, or,
/// while a pairing is under way, the key a pairing may present: any P-256 key on the showing side, and on the entering
/// side only a key whose hash is the pinned device id (PAIR-SPEC.md §2.4). After the handshake, <see cref="Decide"/> is the
/// authority: it accepts a roster key under AMP/1's ALPN and a pairing key under the pairing ALPN, nothing else.
/// </summary>
/// <remarks>
/// The second step exists because not every platform tells the callback the negotiated ALPN (on Windows, .NET 8 throws
/// where .NET 10 answers): while a pairing waits, an unknown key the callback could not refuse by its ALPN completes
/// the handshake and is closed here, before a protocol byte, when the ALPN turns out to be AMP/1's (STRUCTURAL-DEBT.md).
/// </remarks>
internal sealed class PeerCheck
{
    private readonly MeshTlsOptions options;
    private readonly PairingWindow window;
    private readonly IPEndPoint remote;
    private PublicKey? presented;
    private UserId user;
    private bool known;

    /// <summary>The check of one handshake.</summary>
    /// <param name="options">The port's options.</param>
    /// <param name="window">The pairing window read when the handshake began.</param>
    /// <param name="remote">The peer's address: the dialled one on the outbound port.</param>
    public PeerCheck(MeshTlsOptions options, PairingWindow window, IPEndPoint remote)
    {
        this.options = options;
        this.window = window;
        this.remote = remote;
    }

    /// <summary>Whether this handshake is the entering side's pairing dial, which offers only the pairing ALPN.</summary>
    public bool IsEnteringDial => options.Role == TlsRole.Client && window.IsEnteringDial(remote);

    /// <summary>The certificate callback: never throws, refuses whatever it cannot accept.</summary>
    /// <param name="sender">The TLS stream.</param>
    /// <param name="certificate">The peer's certificate, or <see langword="null"/> when it sent none.</param>
    /// <param name="chain">Ignored: no chain counts (SPEC.md §4.3).</param>
    /// <param name="errors">Ignored, for the same reason; a missing certificate is seen as <see langword="null"/>.</param>
    /// <returns><see langword="true"/> to let the handshake complete.</returns>
    public bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        try
        {
            return certificate is not null && Check(sender, certificate);
        }
        catch (Exception)
        {
            // A callback that throws fails the handshake anyway; refusing says the same without the noise.
            return false;
        }
    }

    /// <summary>The authority, after the handshake: who the peer is, or <see langword="null"/> to close without a protocol byte.</summary>
    /// <param name="tls">The authenticated stream.</param>
    /// <param name="now">The pairing window that applies now; a pairing that ended during the handshake admits nothing.</param>
    /// <param name="refusal">Why the peer is refused, for the facts; empty when accepted.</param>
    /// <returns>The peer's identity, or <see langword="null"/>.</returns>
    public PeerIdentity? Decide(SslStream tls, PairingWindow now, out string refusal)
    {
        string alpn = tls.NegotiatedApplicationProtocol == default ? string.Empty : tls.NegotiatedApplicationProtocol.ToString();
        refusal = presented is null ? "the peer presented no acceptable certificate"
            : options.Role == TlsRole.Server && !tls.IsMutuallyAuthenticated ? "the peer presented no client certificate"
            : alpn switch
            {
                Amp.Alpn when known && !IsEnteringDial => string.Empty,
                Amp.Alpn => "the peer's key is in no current roster of a known user",
                PairProtocol.Alpn when options.Role == TlsRole.Server => now.IsShowing ? string.Empty : "no pairing waits",
                PairProtocol.Alpn => now.IsEnteringDial(remote) && now.EnteringTarget!.Admits(presented) ? string.Empty : "the key is not the pinned device's",
                "" => "no ALPN was negotiated",
                _ => "the ALPN " + alpn + " is not spoken here",
            };
        return refusal.Length == 0 ? new PeerIdentity(presented!, known ? user : default, alpn, options.Role, tls.SslProtocol, tls.NegotiatedCipherSuite) : null;
    }

    private bool Check(object sender, X509Certificate certificate)
    {
        if (!DeviceCertificate.TryGetPoint(certificate, out ReadOnlyMemory<byte> point))
            return false;

        TrustSnapshot trust = options.Trust();
        if (trust.TryResolveDevice(point.Span, out DeviceId device, out UserId owner) && trust.TryGetDevice(device, out TrustedDevice trusted))
        {
            presented = trusted.Key;
            user = owner;
            known = true;
            return true;
        }

        if (!MayPair(point.Span, out PublicKey? key))
            return false;

        // Refuse inside the handshake where the platform already tells the ALPN; else Decide refuses after it.
        if (NegotiatedSoFar(sender) is { } alpn && alpn != PairProtocol.Alpn)
            return false;
        presented = key;
        return true;
    }

    // An unknown key is acceptable only to a pairing: on the showing side any P-256 key (PAIR-SPEC.md §4.2), on the
    // entering side exactly a key whose hash is the pinned device's id: the code's key, or the id discovery announced.
    private bool MayPair(ReadOnlySpan<byte> point, out PublicKey? key)
    {
        key = null;
        if (options.Role == TlsRole.Server)
            return window.IsShowing && PublicKey.TryParse(point, out key);
        return IsEnteringDial && window.EnteringTarget!.Admits(point, out key);
    }

    private static string? NegotiatedSoFar(object sender)
    {
        if (sender is not SslStream stream)
            return null;
        try
        {
            SslApplicationProtocol alpn = stream.NegotiatedApplicationProtocol;
            return alpn == default ? null : alpn.ToString();
        }
        catch (InvalidOperationException)
        {
            // .NET 8 on Windows: the handshake is not complete yet, so the stream will not say.
            return null;
        }
    }
}
