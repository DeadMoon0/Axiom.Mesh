using System;
using System.Linq;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §8.2 and §8.3: files as Chunks and ChunkLists, cut in memory or sealed in place.</summary>
public sealed class ChunkerTests
{
    private const int MiB = 1_048_576;

    // §8.2: "File contents are cut into chunks of exactly 1 MiB except the last, which holds the remainder; a zero-length
    // file has no chunks." §8.3: "size is authoritative … and MUST equal the sum of the chunks' sizes".
    [Theory]
    // An empty file.
    [InlineData(0, new int[0])]
    // One byte.
    [InlineData(1, new[] { 1 })]
    // Exactly one MiB.
    [InlineData(MiB, new[] { MiB })]
    // One MiB and one byte.
    [InlineData(MiB + 1, new[] { MiB, 1 })]
    // Two and a half MiB.
    [InlineData((2 * MiB) + (MiB / 2), new[] { MiB, MiB, MiB / 2 })]
    public void AFileIsCutInto1MiBChunksAndARemainder(int length, int[] chunkLengths)
    {
        byte[] content = [.. Enumerable.Range(0, length).Select(i => (byte)(i * 31))];
        ChunkedFile file = Chunker.Cut(content);

        Assert.Equal(chunkLengths, file.Chunks.Select(c => c.Data.Length));
        Assert.Equal((ulong)length, file.List.Size);
        Assert.Equal(file.Chunks.Select(c => c.Hash), file.List.Chunks);
        Assert.Equal(content, file.Chunks.SelectMany(c => c.Data.ToArray()));
        Assert.False(ChunkListRule.TryFindSizeFault(file.List, file.Chunks, out _));
    }

    // §8.1, §8.2: a Chunk sealed in place is the same object, with the same hash, as the Chunk built from the same data;
    // empty data or more than 1 MiB is refused like it.
    [Theory]
    // One byte.
    [InlineData(1)]
    // A remainder.
    [InlineData(5_000)]
    // Exactly one MiB.
    [InlineData(MiB)]
    public void AChunkSealedInPlaceIsTheChunkBuiltFromTheSameData(int length)
    {
        byte[] data = [.. Enumerable.Range(0, length).Select(i => (byte)(i * 7))];
        byte[] buffer = new byte[Chunker.MaxChunkObjectLength];
        data.CopyTo(buffer, Chunker.HeaderLength);

        ObjectHash sealedHash = Chunker.Seal(buffer.AsSpan(0, Chunker.HeaderLength + length));
        Chunk built = ObjectBuilders.Chunk(data);

        Assert.Equal(built.Hash, sealedHash);
        Assert.Equal(built.Bytes.ToArray(), buffer[..(Chunker.HeaderLength + length)]);
        Assert.Throws<ArgumentException>(() => Chunker.Seal(buffer.AsSpan(0, Chunker.HeaderLength)));
        Assert.Throws<ArgumentException>(() => Chunker.Seal(new byte[Chunker.MaxChunkObjectLength + 1]));
    }

    // §8.3: "a receiver that holds the chunks and finds otherwise records the ChunkList as faulty content".
    [Fact]
    public void AChunkListWhoseSizeIsNotTheSumOfItsChunksIsFaulty()
    {
        Chunk chunk = ObjectBuilders.Chunk(new byte[10]);
        ChunkList list = ObjectBuilders.ChunkList(11, [chunk.Hash]);

        Assert.True(ChunkListRule.TryFindSizeFault(list, [chunk], out ObjectFault fault));
        Assert.Equal(ObjectFaultKind.Faulty, fault.Kind);
        Assert.Throws<ArgumentException>(() => ChunkListRule.TryFindSizeFault(list, [ObjectBuilders.Chunk(new byte[11])], out _));
    }
}
