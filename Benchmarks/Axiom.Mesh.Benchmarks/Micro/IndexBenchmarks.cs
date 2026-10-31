using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.Mesh.Core;
using Axiom.Mesh.Core.Storage;
using BenchmarkDotNet.Attributes;

namespace Axiom.Mesh.Benchmarks.Micro;

/// <summary>
/// The index at full durability (plan §5: at least 1 000 Entry commits per second; 1 000 Entries in one transaction
/// within 50 ms). <see cref="EntryRowCommit"/> and <see cref="ThousandEntryRowsInOneTransaction"/> write the Entry and
/// object rows straight into an LDB write transaction; <see cref="EntryThroughTheWriter"/> is the store writer's whole
/// path for one Entry of a linear history: file, rows, Head, pins and counters in one batch.
/// </summary>
[MemoryDiagnoser]
public class IndexBenchmarks
{
    private const int PerIteration = 32;
    private const int Thousand = 1_000;
    private readonly AdmittedEntry[] chain = new AdmittedEntry[PerIteration];
    private DiskStore store = null!;
    private ulong clock;
    private ObjectHash last;
    private ulong seq = 1UL << 40;

    /// <summary>Opens the store.</summary>
    /// <returns>A task.</returns>
    [GlobalSetup]
    public async Task SetupAsync() => store = await DiskStore.StartAsync("index").ConfigureAwait(false);

    /// <summary>Closes and deletes the store.</summary>
    /// <returns>A task.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync() => await store.DisposeAsync().ConfigureAwait(false);

    /// <summary>The next Entries of the linear history, signed and admitted before the iteration starts.</summary>
    [IterationSetup(Target = nameof(EntryThroughTheWriter))]
    public void NextEntries()
    {
        for (int i = 0; i < PerIteration; i++)
        {
            AdmittedEntry entry = store.Entry(clock, clock == 0 ? EntryParents.None : EntryParents.Of(last), $"message {clock}");
            chain[i] = entry;
            last = entry.Hash;
            clock++;
        }
    }

    /// <summary>One Entry row and its object row, committed.</summary>
    /// <returns>A task.</returns>
    [Benchmark]
    public async Task EntryRowCommit()
    {
        await using WriteTransaction transaction = await store.Database.BeginWriteAsync(BatchTables.Names, TransactionOptions.Default, CancellationToken.None).ConfigureAwait(false);
        var tables = new BatchTables(transaction);
        await PutRowsAsync(tables, Interlocked.Increment(ref seq)).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>A thousand Entry rows and their object rows, committed in one transaction.</summary>
    /// <returns>A task.</returns>
    [Benchmark]
    public async Task ThousandEntryRowsInOneTransaction()
    {
        await using WriteTransaction transaction = await store.Database.BeginWriteAsync(BatchTables.Names, TransactionOptions.Default, CancellationToken.None).ConfigureAwait(false);
        var tables = new BatchTables(transaction);
        for (int i = 0; i < Thousand; i++)
            await PutRowsAsync(tables, Interlocked.Increment(ref seq)).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Entries of a linear history stored one after the other through the store writer, each its own batch.</summary>
    /// <returns>A task.</returns>
    [Benchmark(OperationsPerInvoke = PerIteration)]
    public async Task EntryThroughTheWriter()
    {
        foreach (AdmittedEntry entry in chain)
            await store.Writer.SendAsync(new AdmitCommand(entry, Origin.Local, null, null), CancellationToken.None).ConfigureAwait(false);
    }

    // The rows an Entry of 120 bytes adds to the index, under a hash made of its store sequence.
    private static async Task PutRowsAsync(BatchTables tables, ulong storeSeq)
    {
        byte[] hash = RowKeys.Of(Hash32.Of(BitConverter.GetBytes(storeSeq)));
        await tables.Objects.PutAsync(new ObjectRow { Hash = hash, Tag = (byte)ObjectTag.Entry, Size = 120, StoredMs = 1, LastUsedMs = 1 }, CancellationToken.None).ConfigureAwait(false);
        await tables.Entries.PutAsync(new EntryRow
        {
            Hash = hash,
            StoreSeq = storeSeq,
            Package = hash,
            Device = hash,
            Clock = storeSeq,
            Parents = hash,
            Kind = EntryKind.Message,
            StoredMs = 1,
        }, CancellationToken.None).ConfigureAwait(false);
    }
}
