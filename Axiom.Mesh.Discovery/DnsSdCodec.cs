namespace Axiom.Mesh.Discovery;

/// <summary>
/// The DNS-SD form of a node's announcement (SPEC.md §3.1): what a node announces over mDNS and how a record is read
/// back. A record is a hint, never trusted; only the TLS handshake proves who answers.
/// </summary>
public sealed class DnsSdCodec
{
    private DnsSdCodec()
    {
    }

    /// <summary>The DNS-SD service type every node announces (SPEC.md §3.1).</summary>
    public const string ServiceType = "_axiom-mesh._tcp";
}
