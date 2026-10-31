using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// Keyset pages over the stored Entries: by store sequence across every Package (LOCAL-SPEC.md §5.6, §7.2), and by the
/// total order within one Package (SPEC.md §9.3). Each page costs a few index reads however deep it is.
/// </summary>
/// <remarks>
/// LDB 0.1.0 streams an order only through a one-column index of a numeric type; any other order, a byte-string column
/// or several columns included, is sorted in memory over every matching row. The total order is therefore read
/// through the ordered index on the clock, and only the Entries that share the page's last clock are fetched whole and
/// put in order in memory, by <see cref="EntryKey"/>.
/// </remarks>
internal static class EntryIndex
{
    /// <summary>The Entries with a store sequence of at least <paramref name="fromSeq"/>, ascending.</summary>
    /// <param name="context">The unit of work to read in.</param>
    /// <param name="fromSeq">The first store sequence wanted.</param>
    /// <param name="count">At most this many.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The Entries.</returns>
    public static Task<List<EntryRow>> BySeqAsync(LdbContext context, ulong fromSeq, int count, CancellationToken cancellationToken) =>
        context.Set<EntryRow>().ReadOnlyQuery()
            .Where(EntryRow.Columns.StoreSeq >= fromSeq)
            .OrderBy(EntryRow.Columns.StoreSeq)
            .Take(count)
            .ToListAsync(cancellationToken);

    /// <summary>The Entries of a Package after <paramref name="after"/> in the total order.</summary>
    /// <param name="context">The unit of work to read in.</param>
    /// <param name="package">The Package.</param>
    /// <param name="after">The last Entry of the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="count">At most this many; at least 1.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The Entries, in the total order.</returns>
    public static async Task<List<EntryRow>> InOrderAsync(LdbContext context, PackageId package, EntryKey? after, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        byte[] id = RowKeys.Of(package.Value);
        var page = new List<EntryRow>(count);
        ulong fromClock = after?.Clock ?? 0;
        while (page.Count < count)
        {
            int remaining = count - page.Count;
            List<EntryRow> candidates = await context.Set<EntryRow>().ReadOnlyQuery()
                .Where(EntryRow.Columns.Package == id & EntryRow.Columns.Clock >= fromClock)
                .OrderBy(EntryRow.Columns.Clock)
                .Take(remaining + 1)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0)
                break;

            // Rows of one clock come in row order, so the last clock read may be cut: read it whole and order it.
            ulong lastClock = candidates[^1].Clock;
            List<EntryRow> lastClockRows = await context.Set<EntryRow>().ReadOnlyQuery()
                .Where(EntryRow.Columns.Package == id & EntryRow.Columns.Clock == lastClock)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            page.AddRange(candidates.Where(r => r.Clock < lastClock).Concat(lastClockRows)
                .Where(r => after is null || KeyOf(r) > after.Value)
                .OrderBy(KeyOf)
                .Take(remaining));

            bool exhausted = candidates.Count <= remaining;
            if (exhausted || lastClock == ulong.MaxValue)
                break;
            fromClock = lastClock + 1;
        }

        return page;
    }

    /// <summary>The Entries of a Package before <paramref name="before"/> in the total order, highest first (LOCAL-SPEC.md §6.4, <c>reverse</c>).</summary>
    /// <param name="context">The unit of work to read in.</param>
    /// <param name="package">The Package.</param>
    /// <param name="before">The last Entry of the previous page, or <see langword="null"/> for the first page from the end.</param>
    /// <param name="count">At most this many; at least 1.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The Entries, in descending total order.</returns>
    public static async Task<List<EntryRow>> InReverseAsync(LdbContext context, PackageId package, EntryKey? before, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        byte[] id = RowKeys.Of(package.Value);
        var page = new List<EntryRow>(count);
        ulong toClock = before?.Clock ?? ulong.MaxValue;
        while (page.Count < count)
        {
            int remaining = count - page.Count;
            List<EntryRow> candidates = await context.Set<EntryRow>().ReadOnlyQuery()
                .Where(EntryRow.Columns.Package == id & EntryRow.Columns.Clock <= toClock)
                .OrderByDescending(EntryRow.Columns.Clock)
                .Take(remaining + 1)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0)
                break;

            // The lowest clock read may be cut: read it whole and order it.
            ulong lastClock = candidates[^1].Clock;
            List<EntryRow> lastClockRows = await context.Set<EntryRow>().ReadOnlyQuery()
                .Where(EntryRow.Columns.Package == id & EntryRow.Columns.Clock == lastClock)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            page.AddRange(candidates.Where(r => r.Clock > lastClock).Concat(lastClockRows)
                .Where(r => before is null || KeyOf(r) < before.Value)
                .OrderByDescending(KeyOf)
                .Take(remaining));

            bool exhausted = candidates.Count <= remaining;
            if (exhausted || lastClock == 0)
                break;
            toClock = lastClock - 1;
        }

        return page;
    }

    /// <summary>An Entry row's position in the total order.</summary>
    /// <param name="row">The row.</param>
    /// <returns>Its key.</returns>
    public static EntryKey KeyOf(EntryRow row) =>
        new(row.Clock, new DeviceId(RowKeys.Hash(row.Device)), new ObjectHash(RowKeys.Hash(row.Hash)));

    /// <summary>What the parent and redaction rules read of an Entry row (SPEC.md §8.6, §9.4).</summary>
    /// <param name="row">The row.</param>
    /// <returns>Its facts.</returns>
    public static EntryFacts FactsOf(EntryRow row) =>
        new(new PackageId(RowKeys.Hash(row.Package)), row.Target is { } target ? new ObjectHash(RowKeys.Hash(target)) : default, KeyOf(row));
}
