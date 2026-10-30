using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The history and object requests of LOCAL-SPEC.md §6.4: <c>ListHistory</c> (the total order of SPEC.md §9.3, either
/// way, a page at a time), <c>ListStored</c> (store-sequence order across Packages), <c>GetEntry</c> and <c>GetObject</c>
/// (only what is stored). Redacted Entries come with <c>redacted</c> set (§5.6); the client does not display them.
/// </summary>
internal static class HistoryRequests
{
    private const int MaxEntries = 64;

    /// <summary><c>ListHistory</c> (314): the stored Entries of a Package in the total order, after the cursor.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> ListHistoryAsync(RequestContext context, uint correlation, ListHistory request, CancellationToken cancellationToken)
    {
        if (request.Limit is < 1 or > MaxEntries)
            return LocalAnswer.Failure(correlation, Status.Invalid, "The limit is 1 to 64.", "limit");
        if (!PackageRequests.Ascending(request.Kinds.Select(k => (ushort)k)))
            return LocalAnswer.Failure(correlation, Status.Invalid, "The kinds must be ascending and unique.", "kinds");
        MeshNode node = context.Node;
        if (!node.Trust.IsFollowedOrListed(request.PackageId))
            return PackageRequests.Unknown(correlation, request.PackageId);

        var kinds = request.Kinds.ToHashSet();
        var entries = new List<Entry>(request.Limit + 1);
        await using (LdbContext reading = await LdbContext.OpenAsync(node.Writer.Database, cancellationToken).ConfigureAwait(false))
        {
            EntryKey? cursor = request.After;
            while (entries.Count <= request.Limit)
            {
                List<EntryRow> rows = request.Reverse
                    ? await EntryIndex.InReverseAsync(reading, request.PackageId, cursor, MaxEntries, cancellationToken).ConfigureAwait(false)
                    : await EntryIndex.InOrderAsync(reading, request.PackageId, cursor, MaxEntries, cancellationToken).ConfigureAwait(false);
                foreach (EntryRow row in rows.Where(r => kinds.Count == 0 || kinds.Contains(r.Kind)))
                {
                    if (await RecordOfAsync(node.Files, row, cancellationToken).ConfigureAwait(false) is { } entry)
                        entries.Add(entry);
                    if (entries.Count > request.Limit)
                        break;
                }

                if (rows.Count < MaxEntries)
                    break;
                cursor = EntryIndex.KeyOf(rows[^1]);
            }
        }

        (List<Entry> page, bool more) = PageCut.Take(entries, request.Limit, PageCut.FrameBudget, FrameSizes.OfRecord);
        return LocalAnswer.Ok<ListHistory, EntryPage>(correlation, new EntryPage { Entries = page, More = more });
    }

    /// <summary>
    /// <c>ListStored</c> (319): stored Entries of one Package or all, in store-sequence order from <c>fromSeq</c>; in the
    /// service role with no kinds named, the Entry kinds the connection registered (§9.2).
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> ListStoredAsync(RequestContext context, uint correlation, ListStored request, CancellationToken cancellationToken)
    {
        if (request.Limit is < 1 or > MaxEntries)
            return LocalAnswer.Failure(correlation, Status.Invalid, "The limit is 1 to 64.", "limit");
        if (!PackageRequests.Ascending(request.Kinds.Select(k => (ushort)k)))
            return LocalAnswer.Failure(correlation, Status.Invalid, "The kinds must be ascending and unique.", "kinds");

        MeshNode node = context.Node;
        IReadOnlySet<EntryKind> kinds = request.Kinds.Count != 0 || context.Connection.Role != Role.Service
            ? request.Kinds.ToHashSet()
            : context.Port.Kinds.EntryKindsOf(context.Connection);
        bool everyKind = request.Kinds.Count == 0 && context.Connection.Role != Role.Service;
        var entries = new List<Entry>(request.Limit + 1);
        await using (LdbContext reading = await LdbContext.OpenAsync(node.Writer.Database, cancellationToken).ConfigureAwait(false))
        {
            ulong next = Math.Max(1, request.FromSeq);
            while (entries.Count <= request.Limit)
            {
                List<EntryRow> rows = await EntryIndex.BySeqAsync(reading, next, MaxEntries, cancellationToken).ConfigureAwait(false);
                foreach (EntryRow row in rows)
                {
                    bool package = request.PackageId.Value.IsZero || RowKeys.Hash(row.Package) == request.PackageId.Value;
                    if (!package || !(everyKind || kinds.Contains(row.Kind)))
                        continue;
                    if (await RecordOfAsync(node.Files, row, cancellationToken).ConfigureAwait(false) is { } entry)
                        entries.Add(entry);
                    if (entries.Count > request.Limit)
                        break;
                }

                if (rows.Count < MaxEntries)
                    break;
                next = rows[^1].StoreSeq + 1;
            }
        }

        (List<Entry> page, bool more) = PageCut.Take(entries, request.Limit, PageCut.FrameBudget, FrameSizes.OfRecord);
        return LocalAnswer.Ok<ListStored, EntryPage>(correlation, new EntryPage { Entries = page, More = more });
    }

    /// <summary><c>GetEntry</c> (315): a stored Entry; held, evicted and never seen ones are <c>NotFound</c>.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> GetEntryAsync(RequestContext context, uint correlation, GetEntry request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        EntryRow? row;
        await using (LdbContext reading = await LdbContext.OpenAsync(node.Writer.Database, cancellationToken).ConfigureAwait(false))
            row = await reading.Set<EntryRow>().FindAsync(RowKeys.Of(request.Hash.Value), cancellationToken).ConfigureAwait(false);
        if (row is null || await RecordOfAsync(node.Files, row, cancellationToken).ConfigureAwait(false) is not { } entry)
            return NotStored(correlation, request.Hash);
        return LocalAnswer.Ok<GetEntry, Entry>(correlation, entry);
    }

    /// <summary><c>GetObject</c> (316): a stored object of any tag, Rosters included; held, evicted and never seen ones are <c>NotFound</c>.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> GetObjectAsync(RequestContext context, uint correlation, GetObject request, CancellationToken cancellationToken)
    {
        if (!context.Node.Store.TryOpen(request.Hash, out ObjectHandle? handle))
            return NotStored(correlation, request.Hash);
        byte[] bytes;
        using (handle)
        {
            using var copy = new MemoryStream();
            await handle.Stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
            bytes = copy.ToArray();
        }

        return LocalAnswer.Ok<GetObject, ReadOnlyMemory<byte>>(correlation, bytes);
    }

    /// <summary>The record of a stored Entry's row; <see langword="null"/> when its file is gone (evicted meanwhile).</summary>
    /// <param name="files">The object files.</param>
    /// <param name="row">The row.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The record.</returns>
    public static Task<Entry?> RecordOfAsync(IObjectFiles files, EntryRow row, CancellationToken cancellationToken) =>
        EntryRecords.ReadAsync(files, new ObjectHash(RowKeys.Hash(row.Hash)), row.StoreSeq, row.StoredMs, row.Redacted, cancellationToken);

    private static LocalAnswer NotStored(uint correlation, ObjectHash hash) =>
        LocalAnswer.Failure(correlation, Status.NotFound, $"{hash} is not stored here; Fetch it through a followed Package.", "hash");
}
