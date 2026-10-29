namespace Axiom.Mesh.Local;

/// <summary>
/// <c>axiom-mesh-local/1</c>, the protocol every program on a device speaks to the Host over the pipe
/// (LOCAL-SPEC.md).
/// </summary>
public sealed class LocalProtocol
{
    private LocalProtocol()
    {
    }

    /// <summary>The protocol's name and version (LOCAL-SPEC.md §1.5).</summary>
    public const string Name = "axiom-mesh-local/1";
}
