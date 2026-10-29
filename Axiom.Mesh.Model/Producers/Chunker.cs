using System;

namespace Axiom.Mesh;

/// <summary>
/// Cuts file contents into Chunks of exactly 1 MiB except the last, which holds the remainder; an empty file has no
/// Chunks (SPEC.md §8.2). For contents already in memory; the engine streams large files with the same cut.
/// </summary>
public static class Chunker
{
    /// <summary>Cuts <paramref name="content"/> and builds its ChunkList.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <returns>The Chunks and the ChunkList.</returns>
    public static ChunkedFile Cut(ReadOnlyMemory<byte> content)
    {
        int count = (int)(((long)content.Length + Limits.ChunkLength - 1) / Limits.ChunkLength);
        var chunks = new Chunk[count];
        var hashes = new ObjectHash[count];
        for (int i = 0; i < count; i++)
        {
            int start = i * Limits.ChunkLength;
            chunks[i] = ObjectBuilders.Chunk(content.Span.Slice(start, Math.Min(Limits.ChunkLength, content.Length - start)));
            hashes[i] = chunks[i].Hash;
        }

        return new ChunkedFile(ObjectBuilders.ChunkList((ulong)content.Length, hashes), chunks);
    }
}
