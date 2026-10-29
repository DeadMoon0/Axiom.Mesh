using System.Net.Security;
using System.Security.Authentication;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core;

/// <summary>
/// What the TLS handshake proved about the peer of a connection (SPEC.md §4.3): its key, the device and user that key
/// resolves to in the node's trust, the negotiated ALPN and the TLS parameters. Set as a connection feature by
/// <see cref="MeshTlsLayer"/> only when every check passed, so a connection without it never reaches a protocol byte.
/// </summary>
internal sealed class PeerIdentity
{
    /// <summary>The identity a handshake proved.</summary>
    /// <param name="key">The peer's certificate key.</param>
    /// <param name="user">The user whose current Roster lists the key; zero for a pairing peer the node does not know.</param>
    /// <param name="alpn">The negotiated ALPN.</param>
    /// <param name="role">This node's side of the handshake.</param>
    /// <param name="protocol">The TLS version.</param>
    /// <param name="cipherSuite">The negotiated suite.</param>
    public PeerIdentity(PublicKey key, UserId user, string alpn, TlsRole role, SslProtocols protocol, TlsCipherSuite cipherSuite)
    {
        Key = key;
        Device = key.AsDeviceId();
        User = user;
        Alpn = alpn;
        Role = role;
        Protocol = protocol;
        CipherSuite = cipherSuite;
    }

    /// <summary>The peer's device id: <c>H</c> of its certificate key (SPEC.md §2.3).</summary>
    public DeviceId Device { get; }

    /// <summary>The peer's user as the node's trust knows it; zero for a pairing peer the node does not know yet.</summary>
    public UserId User { get; }

    /// <summary>The peer's certificate key, which the handshake proved it holds.</summary>
    public PublicKey Key { get; }

    /// <summary>The negotiated ALPN: <see cref="Amp.Alpn"/> or <see cref="PairProtocol.Alpn"/>.</summary>
    public string Alpn { get; }

    /// <summary>Whether this node dialled (<see cref="TlsRole.Client"/>) or accepted (<see cref="TlsRole.Server"/>).</summary>
    public TlsRole Role { get; }

    /// <summary>The TLS version: always TLS 1.3.</summary>
    public SslProtocols Protocol { get; }

    /// <summary>The negotiated cipher suite.</summary>
    public TlsCipherSuite CipherSuite { get; }

    /// <summary>Whether the connection carries a pairing (PAIR-SPEC.md §4.2), not AMP/1.</summary>
    public bool IsPairing => Alpn == PairProtocol.Alpn;

    /// <inheritdoc />
    public override string ToString() => $"{Device} user {User} {Alpn} as {Role}, {Protocol} {CipherSuite}";
}
