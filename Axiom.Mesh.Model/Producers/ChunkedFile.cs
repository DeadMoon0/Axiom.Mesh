using System.Collections.Generic;

namespace Axiom.Mesh;

/// <summary>A file cut into Chunks, and the ChunkList that names them (SPEC.md §8.2, §8.3).</summary>
public sealed class ChunkedFile
{
    internal ChunkedFile(ChunkList list, IReadOnlyList<Chunk> chunks)
    {
        List = list;
        Chunks = chunks;
    }

    /// <summary>The ChunkList: the file's size and its Chunks' hashes.</summary>
    public ChunkList List { get; }

    /// <summary>The Chunks in file order; none for an empty file.</summary>
    public IReadOnlyList<Chunk> Chunks { get; }
}
