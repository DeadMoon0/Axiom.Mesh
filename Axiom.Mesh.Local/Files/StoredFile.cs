using System.Collections.Generic;

namespace Axiom.Mesh.Local;

/// <summary>
/// A file <see cref="StoreFiles.PutFileAsync"/> stored: its ChunkList (SPEC.md §8.3), which names it, and every object
/// put for it, which carry this service's pins until it releases them (LOCAL-SPEC.md §9.5).
/// </summary>
public sealed record StoredFile
{
    /// <summary>The ChunkList's hash: what a Tree entry or an Entry's <c>references</c> names.</summary>
    public required ObjectHash ChunkList { get; init; }

    /// <summary>The file's size in bytes.</summary>
    public required ulong Size { get; init; }

    /// <summary>The Chunks' hashes in file order; empty for an empty file.</summary>
    public required IReadOnlyList<ObjectHash> Chunks { get; init; }
}
