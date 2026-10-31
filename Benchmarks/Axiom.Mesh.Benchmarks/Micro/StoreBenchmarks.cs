using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using BenchmarkDotNet.Attributes;

namespace Axiom.Mesh.Benchmarks.Micro;

/// <summary>
/// The object store on disk (plan §5): a 1 MiB put at 100 MiB/s or more with its flush, a cached 1 MiB get at 1 GiB/s or
/// more, a small put within 5 ms. A put is the store writer's whole path, one batch per put: the file written to a
/// temporary name, flushed and renamed, then the index row committed at full durability.
/// </summary>
[MemoryDiagnoser]
public class StoreBenchmarks
{
    private const int PerIteration = 16;
    private readonly Chunk[] chunks = new Chunk[PerIteration];
    private readonly Chunk[] small = new Chunk[PerIteration];
    private readonly byte[] read = new byte[Chunker.MaxChunkObjectLength];
    private DiskStore store = null!;
    private Chunk stored = null!;
    private long round;

    /// <summary>Opens the store and stores the object the gets read.</summary>
    /// <returns>A task.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        store = await DiskStore.StartAsync("store").ConfigureAwait(false);
        stored = ObjectBuilders.Chunk(Samples.Bytes(Limits.ChunkLength, -1));
        await store.Store.PutAsync(stored, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Closes and deletes the store.</summary>
    /// <returns>A task.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync() => await store.DisposeAsync().ConfigureAwait(false);

    /// <summary>New objects for the next iteration: a put of an object already stored costs nothing.</summary>
    [IterationSetup(Targets = [nameof(Put1MiB), nameof(PutSmall)])]
    public void NewObjects()
    {
        byte[] data = new byte[Limits.ChunkLength];
        for (int i = 0; i < PerIteration; i++)
        {
            long seed = Interlocked.Increment(ref round);
            Samples.Stamp(data, seed);
            chunks[i] = ObjectBuilders.Chunk(data);
            small[i] = ObjectBuilders.Chunk(data.AsSpan(0, 100));
        }
    }

    /// <summary>Puts 1 MiB Chunks one after the other, each its own batch.</summary>
    /// <returns>A task.</returns>
    [Benchmark(OperationsPerInvoke = PerIteration)]
    public async Task Put1MiB()
    {
        foreach (Chunk chunk in chunks)
            await store.Store.PutAsync(chunk, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Puts 100-byte Chunks one after the other, each its own batch.</summary>
    /// <returns>A task.</returns>
    [Benchmark(OperationsPerInvoke = PerIteration)]
    public async Task PutSmall()
    {
        foreach (Chunk chunk in small)
            await store.Store.PutAsync(chunk, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Opens a stored 1 MiB object and reads it whole, as a peer's <c>Want</c> is answered.</summary>
    /// <returns>The bytes read.</returns>
    [Benchmark]
    public int Get1MiB()
    {
        if (!store.Store.TryOpen(stored.Hash, out ObjectHandle? handle))
            throw new InvalidOperationException("The stored object is gone.");
        using (handle)
        {
            int length = (int)handle.Length;
            handle.Stream.ReadExactly(read.AsSpan(0, length));
            return length;
        }
    }
}
