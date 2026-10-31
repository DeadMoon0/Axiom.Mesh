using System.Collections.Generic;

namespace Axiom.Mesh.Local;

/// <summary>
/// A directory <see cref="Trees.PublishDirectoryAsync"/> stored: its root Tree, which an Entry references, and every
/// object put for it (Chunks, ChunkLists, Trees), which carry this service's pins until it releases them with
/// <c>Unpin</c> once the Entry is written (LOCAL-SPEC.md §9.5).
/// </summary>
public sealed record PublishedTree
{
    /// <summary>The root Tree's hash.</summary>
    public required ObjectHash Root { get; init; }

    /// <summary>How many regular files the directory held.</summary>
    public required int Files { get; init; }

    /// <summary>Their bytes in all.</summary>
    public required ulong Bytes { get; init; }

    /// <summary>Every object put, each once, ascending.</summary>
    public required IReadOnlyList<ObjectHash> Objects { get; init; }
}
