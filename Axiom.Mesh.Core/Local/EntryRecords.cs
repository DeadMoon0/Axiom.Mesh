using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// Reads stored Entries back as LOCAL-SPEC.md §5.6 records: the index row gives the store sequence, the time it was
/// stored and the redaction mark, the object file the parents and the payload. The object was verified when it was
/// stored, so it is only parsed here.
/// </summary>
internal static class EntryRecords
{
    /// <summary>The record of a stored Entry.</summary>
    /// <param name="files">The object files.</param>
    /// <param name="hash">The Entry's hash.</param>
    /// <param name="storeSeq">Its store sequence.</param>
    /// <param name="storedMs">When it was stored.</param>
    /// <param name="redacted">Whether a stored Redaction withdraws it.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The record; <see langword="null"/> when its file is gone (evicted meanwhile).</returns>
    public static async Task<Entry?> ReadAsync(IObjectFiles files, ObjectHash hash, ulong storeSeq, long storedMs, bool redacted, CancellationToken cancellationToken)
    {
        byte[]? bytes = await StoredObjects.ReadAsync(files, hash, cancellationToken).ConfigureAwait(false);
        if (bytes is null || !HashedObject.TryVerify(bytes, hash, out HashedObject hashed)
            || !Objects.TryParse(hashed, out ParsedObject? parsed, out _) || parsed is not UnverifiedEntry entry)
            return null;
        return LocalRecords.EntryOf(entry.Content, storeSeq, storedMs, redacted);
    }

    /// <summary>The Entry stored with a store sequence, as an <c>EntryStored</c> event with its frame's size.</summary>
    /// <param name="database">The index.</param>
    /// <param name="files">The object files.</param>
    /// <param name="storeSeq">The store sequence.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The event; <see langword="null"/> when no Entry holds the sequence any more.</returns>
    public static async Task<QueuedEvent?> EventAtAsync(Database database, IObjectFiles files, ulong storeSeq, CancellationToken cancellationToken)
    {
        await using LdbContext context = await LdbContext.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        List<EntryRow> rows = await EntryIndex.BySeqAsync(context, storeSeq, 1, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0 || rows[0].StoreSeq != storeSeq)
            return null;
        EntryRow row = rows[0];
        if (await ReadAsync(files, new ObjectHash(RowKeys.Hash(row.Hash)), row.StoreSeq, row.StoredMs, row.Redacted, cancellationToken).ConfigureAwait(false) is not { } entry)
            return null;
        var stored = new EntryStored { Entry = entry };
        return new QueuedEvent(stored, FrameSizes.OfEvent(stored), storeSeq);
    }

    /// <summary>
    /// The store sequences a replay delivers (LOCAL-SPEC.md §7.2): every stored Entry the filter matches with a sequence
    /// from <paramref name="fromSeq"/> to <paramref name="upTo"/>, ascending, read as numbers only.
    /// </summary>
    /// <param name="database">The index.</param>
    /// <param name="filter">The subscription's filter.</param>
    /// <param name="fromSeq">The first store sequence wanted.</param>
    /// <param name="upTo">The newest store sequence when the subscription began; later Entries arrive live.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The sequences; more than 256 means <c>GapTooLarge</c>, and the list stops at 257.</returns>
    public static async Task<List<ulong>> ReplayPlanAsync(Database database, Filter filter, ulong fromSeq, ulong upTo, CancellationToken cancellationToken)
    {
        var plan = new List<ulong>();
        await using LdbContext context = await LdbContext.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        ulong next = fromSeq;
        while (next <= upTo && plan.Count <= Limits.MaxReplayEntries)
        {
            List<EntryRow> page = await EntryIndex.BySeqAsync(context, next, Limits.MaxReplayEntries, cancellationToken).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            foreach (EntryRow row in page)
            {
                if (row.StoreSeq > upTo || plan.Count > Limits.MaxReplayEntries)
                    return plan;
                if (FilterRule.Matches(filter, new PackageId(RowKeys.Hash(row.Package)), (ushort)row.Kind))
                    plan.Add(row.StoreSeq);
            }

            next = page[^1].StoreSeq + 1;
        }

        return plan;
    }
}
