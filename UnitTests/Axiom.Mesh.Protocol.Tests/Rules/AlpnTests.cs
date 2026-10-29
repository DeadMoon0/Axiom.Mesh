namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>The name a mesh connection negotiates (SPEC.md §4.2, §14.4).</summary>
public sealed class AlpnTests
{
    // SPEC.md §14.4: "axiom-mesh/1 | this protocol"; a connection without it is closed (§4.2).
    [Fact]
    public void AMeshConnectionNegotiatesAxiomMesh1()
    {
        Assert.Equal("axiom-mesh/1", Amp.Alpn);
    }
}
