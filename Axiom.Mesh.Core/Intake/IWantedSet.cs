namespace Axiom.Mesh.Core;

/// <summary>
/// What this node asked its peers for. An unsigned object (Chunk, ChunkList, Tree, opaque) needs nothing but its hash
/// to be stored (SPEC.md §10.6), so intake stores one only when it was wanted, and drops the rest; replication's want
/// scheduler answers this, tests hand in a set.
/// </summary>
internal interface IWantedSet
{
    /// <summary>Whether the node wants the object.</summary>
    /// <param name="hash">The object's hash.</param>
    /// <returns><see langword="true"/> when an outstanding <c>Want</c> or a local fetch asked for it.</returns>
    bool IsWanted(ObjectHash hash);
}
