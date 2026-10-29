using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Tests.Shared;
using Xunit.Abstractions;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>
/// The LDB probe of the fundamentals plan §2.5: 100 000 Entry rows inserted in batches, then paged by store sequence and
/// by the total order. The numbers go to the test output; the bounds are generous, they catch an order that fell back
/// to sorting the whole Package on every page.
/// </summary>
public sealed class EntryIndexProbeTests(ITestOutputHelper output)
{
    private const int Entries = 100_000;
    private const int Batch = 1_000;
    private const int Page = 64;
    private const int Devices = 4;

    // Damage: a page of the history or of the store sequence that sorted every Entry of the Package in memory would
    // make a walk of 100 000 Entries quadratic; both walks must stay keyset pages over an index.
    [ReleaseFact]
    public async Task OneHundredThousandEntriesPageBySeqAndByTotalOrderThroughAnIndex()
    {
        CancellationToken ct = CancellationToken.None;
        using var environment = new InMemoryStorageEnvironment();
        await using Database database = await Database.OpenAsync(environment, DatabaseOptions.Default, ct);
        await using MeshDatabase mesh = await MeshDatabase.OpenAsync(database, new DeterministicEntropy(42), ct);
        PackageId package = new(Hash(1, 0));

        var clock = Stopwatch.StartNew();
        for (int start = 0; start < Entries; start += Batch)
        {
            await using LdbContext context = await LdbContext.OpenAsync(database, ct);
            for (int i = start; i < start + Batch; i++)
                context.Set<EntryRow>().Add(Row(package, i));
            await context.SaveChangesAsync(ct);
        }

        double insertSeconds = clock.Elapsed.TotalSeconds;

        clock.Restart();
        ulong expectedSeq = 1;
        await using (LdbContext context = await LdbContext.OpenAsync(database, ct))
        {
            for (List<EntryRow> rows = await EntryIndex.BySeqAsync(context, 1, Page, ct); rows.Count > 0; rows = await EntryIndex.BySeqAsync(context, expectedSeq, Page, ct))
            {
                foreach (EntryRow row in rows)
                    Assert.Equal(expectedSeq++, row.StoreSeq);
            }
        }

        double seqSeconds = clock.Elapsed.TotalSeconds;
        Assert.Equal((ulong)Entries + 1, expectedSeq);

        clock.Restart();
        int seen = 0;
        EntryKey? last = null;
        await using (LdbContext context = await LdbContext.OpenAsync(database, ct))
        {
            for (List<EntryRow> rows = await EntryIndex.InOrderAsync(context, package, null, Page, ct); rows.Count > 0; rows = await EntryIndex.InOrderAsync(context, package, last, Page, ct))
            {
                foreach (EntryRow row in rows)
                {
                    EntryKey key = EntryIndex.KeyOf(row);
                    Assert.True(last is null || key > last.Value, "The total order went backwards.");
                    last = key;
                    seen++;
                }
            }
        }

        double orderSeconds = clock.Elapsed.TotalSeconds;
        Assert.Equal(Entries, seen);

        clock.Restart();
        await using (LdbContext context = await LdbContext.OpenAsync(database, ct))
        {
            PageCursor? cursor = null;
            for (int p = 0; p < 4; p++)
            {
                var sorted = await context.Set<EntryRow>().ReadOnlyQuery()
                    .Where(EntryRow.Columns.Package == RowKeys.Of(package.Value))
                    .OrderBy(EntryRow.Columns.Clock)
                    .ThenBy(EntryRow.Columns.Device.Ascending)
                    .ThenBy(EntryRow.Columns.Hash.Ascending)
                    .ReadPageAsync(Page, cursor, ct);
                cursor = sorted.Next;
            }
        }

        double sortedPageMs = clock.Elapsed.TotalMilliseconds / 4;

        output.WriteLine($"insert {Entries} rows in batches of {Batch}: {insertSeconds:F2} s ({Entries / insertSeconds:F0} rows/s, {Entries / Batch / insertSeconds:F1} commits/s)");
        output.WriteLine($"walk by store sequence, pages of {Page}: {seqSeconds:F2} s ({Entries / Page} pages, {seqSeconds * 1000 / (Entries / Page):F2} ms/page)");
        output.WriteLine($"walk by total order through the clock index, pages of {Page}: {orderSeconds:F2} s ({orderSeconds * 1000 / (Entries / Page):F2} ms/page)");
        output.WriteLine($"one page by OrderBy(Clock).ThenBy(Device).ThenBy(Hash), sorted in memory: {sortedPageMs:F1} ms/page");
        Assert.True(seqSeconds < 60 && orderSeconds < 60, "A keyset walk of 100 000 Entries took more than a minute.");
    }

    private static EntryRow Row(PackageId package, int i) => new()
    {
        Hash = RowKeys.Of(Hash((uint)i, 7)),
        StoreSeq = (ulong)i + 1,
        Package = RowKeys.Of(package.Value),
        Device = RowKeys.Of(Hash((uint)(i % Devices), 3)),
        Clock = (ulong)(i / Devices),
        Parents = [],
        Kind = EntryKind.Message,
        StoredMs = i,
    };

    private static Hash32 Hash(uint value, byte salt)
    {
        byte[] bytes = new byte[Hash32.Length];
        bytes[0] = salt;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(1), value * 2654435761u);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(5), value);
        return Hash32.Of(bytes);
    }
}
