using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>SPEC.md §8.2 and §8.3: a file is cut into Chunks of exactly 1 MiB and a remainder, named by one ChunkList.</summary>
public sealed class ChunkStreamerTests
{
    // SPEC.md §8.2: Chunks of exactly 1 MiB except the last, which holds the remainder; an empty file has none. The
    // streaming cut gives the same objects as Model's in-memory one, and hands each Chunk on before reading the next.
    [Theory]
    [InlineData(0)] // an empty file: a ChunkList without Chunks
    [InlineData(1)] // one byte
    [InlineData(Limits.ChunkLength)] // exactly one Chunk
    [InlineData(Limits.ChunkLength + 1)] // one full Chunk and a one-byte remainder
    [InlineData((3 * Limits.ChunkLength) + 12_345)] // three full Chunks and a remainder
    public async Task AStreamIsCutLikeTheChunkerInto1MiBChunksAndARemainder_OneChunkAtATime(int length)
    {
        byte[] content = new byte[length];
        new Random(length).NextBytes(content);
        ChunkedFile expected = Chunker.Cut(content);
        using var stream = new MemoryStream(content, writable: false);
        var seen = new List<(ObjectHash Hash, long ReadSoFar)>();

        ChunkList list = await ChunkStreamer.CutAsync(stream, (chunk, _) =>
        {
            seen.Add((chunk.Hash, stream.Position));
            return ValueTask.CompletedTask;
        }, default);

        Assert.Equal(expected.List.Hash, list.Hash);
        Assert.Equal(expected.Chunks.Select(c => c.Hash), seen.Select(s => s.Hash));
        Assert.All(seen.Select((s, i) => (s.ReadSoFar, Index: i)), s => Assert.True(s.ReadSoFar <= (long)(s.Index + 1) * Limits.ChunkLength, "The file was read ahead of the Chunk handed on."));
    }
}
