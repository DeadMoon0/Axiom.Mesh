namespace Axiom.Mesh.Discovery.Tests.Rules;

/// <summary>What a node announces over DNS-SD (SPEC.md §3.1).</summary>
public sealed class ServiceTypeTests
{
    // SPEC.md §3.1: "A node SHOULD announce the service _axiom-mesh._tcp".
    [Fact]
    public void ANodeAnnouncesTheServiceAxiomMeshOverTcp()
    {
        Assert.Equal("_axiom-mesh._tcp", DnsSdCodec.ServiceType);
    }
}
