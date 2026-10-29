using System;
using System.Linq;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core.Tests.Conventions;

/// <summary>What Axiom.Mesh.Core shows the world: the engine is internal; the Host and Testing get only what they plug in.</summary>
public sealed class PublicTypeTests
{
    /// <summary>
    /// Every public type of the package, with who needs it. Adding one is a decision about the surface, made here.
    /// </summary>
    private static readonly Type[] Allowed =
    [
        typeof(MeshNode), // the facade the Host starts and Testing runs
        typeof(MeshStoreException), // what the Host shows when the store cannot open
        typeof(IEntropy), // the Host and Testing choose the randomness
        typeof(SystemEntropy), // the Host's
        typeof(DeterministicEntropy), // Testing's
        typeof(IObjectFiles), // the Host and Testing choose where objects live
        typeof(ObjectDeletion), // what an IObjectFiles answers on delete
        typeof(DirectoryObjectFiles), // the Host's
        typeof(InMemoryObjectFiles), // Testing's
    ];

    // The store, the writer, trust, intake and the entities are the engine's; the Host and Testing reach them through the
    // node. A public type nobody outside needs is surface somebody must keep compatible for ever.
    [Fact]
    public void NoTypeOfCoreIsPublicUnlessHostOrTestingNeedsIt()
    {
        Type[] exported = typeof(MeshNode).Assembly.GetExportedTypes();

        Assert.Equal(Allowed.Select(t => t.FullName).Order(), exported.Select(t => t.FullName).Order());
    }
}
