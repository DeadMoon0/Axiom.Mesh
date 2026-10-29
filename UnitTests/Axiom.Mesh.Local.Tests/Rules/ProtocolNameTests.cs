namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>The local protocol's name and version (LOCAL-SPEC.md, title and §1.5).</summary>
public sealed class ProtocolNameTests
{
    [Fact]
    public void TheLocalProtocolIsAxiomMeshLocal1()
    {
        Assert.Equal("axiom-mesh-local/1", LocalProtocol.Name);
    }
}
