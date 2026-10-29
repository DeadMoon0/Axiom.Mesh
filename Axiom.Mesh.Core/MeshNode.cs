namespace Axiom.Mesh.Core;

/// <summary>
/// One node of a mesh: its store and index, its trust in rosters and grants, its connections to peers and the local
/// port its clients reach. Everything dishonest a node needs (files, database, keys, clock, sockets) is handed to it,
/// so the same node runs on disk and sockets or entirely in memory.
/// </summary>
/// <remarks>Not startable yet: the engine is built milestone by milestone (store, transport, replication, local port).</remarks>
public sealed class MeshNode
{
    private MeshNode()
    {
    }
}
