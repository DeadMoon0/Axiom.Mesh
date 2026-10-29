using System;
using System.Collections.Generic;

namespace Axiom.Mesh;

/// <summary>The rule a ChunkList can only be checked against once its Chunks are held (SPEC.md §8.3, §11.4).</summary>
public static class ChunkListRule
{
    /// <summary>
    /// Whether the ChunkList's size equals the sum of its Chunks' lengths; a node that finds otherwise records the
    /// ChunkList as faulty content (SPEC.md §8.3, §12.2) and writes no file from it (§11.4).
    /// </summary>
    /// <param name="list">The ChunkList.</param>
    /// <param name="chunks">Its Chunks as held, one per hash, in order.</param>
    /// <param name="fault">The fault, when the sizes differ.</param>
    /// <returns><see langword="true"/> when the ChunkList is faulty.</returns>
    /// <exception cref="ArgumentException"><paramref name="chunks"/> are not the ChunkList's Chunks in order.</exception>
    public static bool TryFindSizeFault(ChunkList list, IReadOnlyList<Chunk> chunks, out ObjectFault fault)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count != list.Chunks.Count)
            throw new ArgumentException($"The ChunkList names {list.Chunks.Count} Chunks; {chunks.Count} were given.", nameof(chunks));

        ulong total = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].Hash != list.Chunks[i])
                throw new ArgumentException($"Chunk {i} is {chunks[i].Hash}, but the ChunkList names {list.Chunks[i]}.", nameof(chunks));
            total += (ulong)chunks[i].Data.Length;
        }

        fault = total == list.Size ? default : ObjectFault.Faulty($"A ChunkList's size is {list.Size}, its Chunks hold {total} bytes (SPEC.md §8.3).");
        return total != list.Size;
    }
}
