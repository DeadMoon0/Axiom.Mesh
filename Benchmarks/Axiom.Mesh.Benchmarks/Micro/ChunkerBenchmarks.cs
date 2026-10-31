using System;
using BenchmarkDotNet.Attributes;

namespace Axiom.Mesh.Benchmarks.Micro;

/// <summary>
/// Cutting and hashing 64 MiB (plan §5: at least 800 MiB/s, no allocation per Chunk in steady state): the streaming cut a
/// file helper runs (each 1 MiB copied into one reused buffer, as a file read would, and sealed in place), and the
/// in-memory <see cref="Chunker.Cut"/>, which builds every Chunk as an object of its own.
/// </summary>
[MemoryDiagnoser]
public class ChunkerBenchmarks
{
    private const int Length = 64 * Limits.ChunkLength;
    private readonly byte[] content = Samples.Bytes(Length, 2);
    private readonly byte[] buffer = new byte[Chunker.MaxChunkObjectLength];

    /// <summary>64 Chunks cut into one reused buffer and sealed: what <c>StoreFiles.PutFileAsync</c> does per Chunk.</summary>
    /// <returns>The last Chunk's hash.</returns>
    [Benchmark(Baseline = true)]
    public ObjectHash CutAndSeal64MiB()
    {
        ObjectHash last = default;
        for (int start = 0; start < Length; start += Limits.ChunkLength)
        {
            int part = Math.Min(Limits.ChunkLength, Length - start);
            content.AsSpan(start, part).CopyTo(buffer.AsSpan(Chunker.HeaderLength));
            last = Chunker.Seal(buffer.AsSpan(0, Chunker.HeaderLength + part));
        }

        return last;
    }

    /// <summary>64 MiB cut in memory, each Chunk an object owning its bytes, and the ChunkList built.</summary>
    /// <returns>The ChunkList's hash.</returns>
    [Benchmark]
    public ObjectHash CutInMemory64MiB() => Chunker.Cut(content).List.Hash;
}
