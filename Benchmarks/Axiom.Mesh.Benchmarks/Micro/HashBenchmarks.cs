using System;
using BenchmarkDotNet.Attributes;

namespace Axiom.Mesh.Benchmarks.Micro;

/// <summary>SHA-256 of one 1 MiB Chunk (plan §5: at least 1 GiB/s per core).</summary>
[MemoryDiagnoser]
public class HashBenchmarks
{
    private readonly byte[] chunk = Samples.Bytes(Limits.ChunkLength, 1);

    /// <summary>H(1 MiB).</summary>
    /// <returns>The hash.</returns>
    [Benchmark]
    public Hash32 Sha256Of1MiB() => Hash32.Of(chunk);
}
