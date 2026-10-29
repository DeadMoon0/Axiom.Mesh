using System.Collections.Generic;

namespace Axiom.Mesh.Testing;

/// <summary>
/// A folder made of objects (SPEC.md §8.2–§8.4): its root Tree, and every object it consists of in the order a writer
/// stores them, Chunks before their ChunkList and every object before the Tree that names it.
/// </summary>
public sealed class TreeFixture
{
    internal TreeFixture(Tree root, IReadOnlyList<StorableObject> objects, IReadOnlyList<Chunk> chunks)
    {
        Root = root;
        Objects = objects;
        Chunks = chunks;
    }

    /// <summary>The root Tree, which a <c>Version</c> names.</summary>
    public Tree Root { get; }

    /// <summary>Every object of the folder, the root last.</summary>
    public IReadOnlyList<StorableObject> Objects { get; }

    /// <summary>Every Chunk of every file.</summary>
    public IReadOnlyList<Chunk> Chunks { get; }

    /// <inheritdoc />
    public override string ToString() => $"Tree {Root.Hash} of {Objects.Count} objects";
}
