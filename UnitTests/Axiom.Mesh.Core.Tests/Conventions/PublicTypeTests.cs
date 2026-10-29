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
        typeof(MeshNodeOptions), // what the Host and Testing tell a node about itself
        typeof(MeshNodeEnvironment), // the dishonest parts the Host and Testing hand a node
        typeof(MeshNodeException), // what the Host shows when a node cannot start
        typeof(NodeIdentity), // MeshNode.Identity
        typeof(NodeFacts), // MeshNode.Facts: the Host logs them, Testing waits on them
        typeof(NodeFact), // one of them
        typeof(NodeFactKind), // its kind
        typeof(PeerRecord), // MeshNode.Peers: the CLI's peers list, Testing's waits
        typeof(PeerState), // where a peer stands
        typeof(ReplicationStatus), // MeshNode.Replication: Testing settles on it, the Host's Sessions request reads it
        typeof(SessionTraffic), // one session in it (LOCAL-SPEC.md §5.8)
        typeof(MeshStoreException), // what the Host shows when the store cannot open
        typeof(IEntropy), // the Host and Testing choose the randomness
        typeof(SystemEntropy), // the Host's
        typeof(DeterministicEntropy), // Testing's
        typeof(IKeyStore), // the Host and Testing choose where keys live
        typeof(KeyRole), // which key
        typeof(IDialer), // the Host and Testing choose how peers are dialled
        typeof(TcpDialer), // the Host's
        typeof(DeviceCertificate), // Testing's scripted peers present one; the Host shows its key
        typeof(IObjectFiles), // the Host and Testing choose where objects live
        typeof(ObjectDeletion), // what an IObjectFiles answers on delete
        typeof(DirectoryObjectFiles), // the Host's
        typeof(InMemoryObjectFiles), // Testing's
    ];

    // The store, the writer, trust, intake, the transport and the entities are the engine's; the Host and Testing reach
    // them through the node. A public type nobody outside needs is surface somebody must keep compatible for ever.
    [Fact]
    public void NoTypeOfCoreIsPublicUnlessHostOrTestingNeedsIt()
    {
        Type[] exported = typeof(MeshNode).Assembly.GetExportedTypes();

        Assert.Equal(Allowed.Select(t => t.FullName).Order(), exported.Select(t => t.FullName).Order());
    }
}
