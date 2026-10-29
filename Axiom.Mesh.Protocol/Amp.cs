namespace Axiom.Mesh.Protocol;

/// <summary>
/// AMP/1, the protocol two nodes of a mesh speak over one mutually authenticated TLS connection (SPEC.md).
/// </summary>
public sealed class Amp
{
    private Amp()
    {
    }

    /// <summary>The ALPN a mesh connection negotiates, and the protocol's name (SPEC.md §4.2, §14.4).</summary>
    public const string Alpn = "axiom-mesh/1";
}
