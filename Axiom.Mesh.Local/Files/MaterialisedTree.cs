using System.Collections.Generic;

namespace Axiom.Mesh.Local;

/// <summary>What <see cref="StoreFiles.MaterialiseAsync"/> wrote.</summary>
public sealed record MaterialisedTree
{
    /// <summary>The Tree written.</summary>
    public required ObjectHash Tree { get; init; }

    /// <summary>The files written.</summary>
    public required int Files { get; init; }

    /// <summary>Their bytes in all.</summary>
    public required ulong Bytes { get; init; }

    /// <summary>
    /// The paths not written, relative with <c>/</c> (SPEC.md §8.4, §11.4): names this file system cannot hold, names
    /// colliding case-insensitively, and files whose Chunks do not add up to their ChunkList's size.
    /// </summary>
    public required IReadOnlyList<string> Conflicts { get; init; }
}
