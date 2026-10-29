using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>SPEC.md §9.3 (the total order) and LOCAL-SPEC.md §5.6 (the store sequence), as the index pages them.</summary>
public sealed class EntryIndexTests
{
    // SPEC.md §9.3: Entries order by (clock, device id, hash) ascending; a keyset walk of any page size yields exactly
    // that order, ties of one clock included, and only the Package asked for. LOCAL-SPEC.md §5.6: by store sequence, the
    // order stored.
    [Theory]
    [InlineData(1)] // one Entry per page: every boundary falls inside a clock
    [InlineData(3)] // a page cuts the four Entries of a clock
    [InlineData(64)] // the history page of LOCAL-SPEC.md §12
    public async Task PagesWalkTheTotalOrderAndTheStoreSequenceExactly(int pageSize)
    {
        await using TestStore node = await TestStore.StartAsync();
        var random = new Random(pageSize);
        PackageId package = new(Hash32.Of([1]));
        PackageId other = new(Hash32.Of([2]));
        var rows = new List<EntryRow>();
        await using (LdbContext context = await LdbContext.OpenAsync(node.Database, CancellationToken.None))
        {
            for (int i = 0; i < 200; i++)
            {
                byte[] hash = new byte[Hash32.Length];
                random.NextBytes(hash);
                var row = new EntryRow
                {
                    Hash = hash,
                    StoreSeq = (ulong)i + 1,
                    Package = RowKeys.Of((i % 5 == 0 ? other : package).Value),
                    Device = RowKeys.Of(Hash32.Of([(byte)random.Next(4)])),
                    Clock = (ulong)random.Next(40),
                    Parents = [],
                    Kind = EntryKind.Message,
                };
                rows.Add(row);
                context.Set<EntryRow>().Add(row);
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }

        var walked = new List<EntryKey>();
        var bySeq = new List<ulong>();
        await using (LdbContext context = await LdbContext.OpenAsync(node.Database, CancellationToken.None))
        {
            EntryKey? after = null;
            for (List<EntryRow> page = await EntryIndex.InOrderAsync(context, package, null, pageSize, default); page.Count > 0; page = await EntryIndex.InOrderAsync(context, package, after, pageSize, default))
            {
                Assert.True(page.Count <= pageSize);
                walked.AddRange(page.Select(EntryIndex.KeyOf));
                after = walked[^1];
            }

            ulong from = 1;
            for (List<EntryRow> page = await EntryIndex.BySeqAsync(context, from, pageSize, default); page.Count > 0; page = await EntryIndex.BySeqAsync(context, from, pageSize, default))
            {
                bySeq.AddRange(page.Select(r => r.StoreSeq));
                from = page[^1].StoreSeq + 1;
            }
        }

        EntryKey[] expected = [.. rows.Where(r => r.Package.AsSpan().SequenceEqual(RowKeys.Of(package.Value))).Select(EntryIndex.KeyOf).Order()];
        Assert.Equal(expected, walked);
        Assert.Equal(Enumerable.Range(1, 200).Select(i => (ulong)i), bySeq);
    }
}
