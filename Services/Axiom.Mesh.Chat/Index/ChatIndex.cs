using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The chat service's index (CHAT-SERVICE.md §10): Channels, messages, attachments, redactions and pending attachment pins,
/// rows in the service's own database. A cache of what the Host stores: rebuilt by store sequence, discarded on another
/// store epoch, every write deduplicated by the Entry's hash so a replay changes nothing. One operation at a time; each
/// write is one transaction.
/// </summary>
internal sealed class ChatIndex
{
    private const string MetaName = "meta";

    private readonly Database database;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>An index over a database whose tables exist (<see cref="ChatDatabase.OpenAsync"/>).</summary>
    /// <param name="database">The database.</param>
    internal ChatIndex(Database database) => this.database = database;

    /// <summary>What the meta row says.</summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The meta facts; schema 0 when there is no meta row.</returns>
    public Task<IndexMeta> ReadMetaAsync(CancellationToken cancellationToken) => ReadAsync(async context =>
    {
        MetaRow? row = await context.Set<MetaRow>().FindAsync(MetaName, cancellationToken).ConfigureAwait(false);
        return row is null ? new IndexMeta(0, default, 0) : new IndexMeta(row.SchemaVersion, RowBytes.Hash(row.StoreEpoch), row.LastSeq);
    }, cancellationToken);

    /// <summary>Records the epoch the index is built under, keeping its rows (a fresh index, or the same epoch).</summary>
    /// <param name="epoch">The Host's store epoch.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is committed.</returns>
    public Task SetEpochAsync(Hash32 epoch, CancellationToken cancellationToken) => WriteAsync(async context =>
    {
        MetaRow meta = await MetaAsync(context, cancellationToken).ConfigureAwait(false);
        meta.SchemaVersion = CatchUpPlan.SchemaVersion;
        meta.StoreEpoch = RowBytes.Of(epoch);
    }, cancellationToken);

    /// <summary>Drops every Channel, message, attachment and redaction and starts over under an epoch; pending pins stay.</summary>
    /// <param name="epoch">The Host's store epoch.</param>
    /// <param name="lastSeq">The store sequence the index counts as taken in: 0 for a replay from the start, the runtime's cursor for a rebuild.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is committed.</returns>
    public Task DiscardAsync(Hash32 epoch, ulong lastSeq, CancellationToken cancellationToken) => WriteAsync(async context =>
    {
        await RemoveAllAsync(context.Set<MessageRow>(), cancellationToken).ConfigureAwait(false);
        await RemoveAllAsync(context.Set<AttachmentRow>(), cancellationToken).ConfigureAwait(false);
        await RemoveAllAsync(context.Set<RedactionRow>(), cancellationToken).ConfigureAwait(false);
        await RemoveAllAsync(context.Set<ChannelRow>(), cancellationToken).ConfigureAwait(false);
        MetaRow meta = await MetaAsync(context, cancellationToken).ConfigureAwait(false);
        meta.SchemaVersion = CatchUpPlan.SchemaVersion;
        meta.StoreEpoch = RowBytes.Of(epoch);
        meta.LastSeq = lastSeq;
    }, cancellationToken);

    /// <summary>Records that the stream reached a store sequence without an Entry the index keeps.</summary>
    /// <param name="storeSeq">The store sequence.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is committed.</returns>
    public Task AdvanceAsync(ulong storeSeq, CancellationToken cancellationToken) => WriteLazyAsync(async context =>
    {
        MetaRow meta = await MetaAsync(context, cancellationToken).ConfigureAwait(false);
        meta.LastSeq = Math.Max(meta.LastSeq, storeSeq);
    }, cancellationToken);

    /// <summary>One Channel's row.</summary>
    /// <param name="package">The Channel.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The row, or <see langword="null"/>.</returns>
    public Task<ChannelRow?> ChannelAsync(PackageId package, CancellationToken cancellationToken) =>
        ReadAsync(context => context.Set<ChannelRow>().FindAsync(RowBytes.Of(package.Value), cancellationToken), cancellationToken);

    /// <summary>Every Channel's row.</summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The rows.</returns>
    public Task<List<ChannelRow>> ChannelsAsync(CancellationToken cancellationToken) =>
        ReadAsync(context => context.Set<ChannelRow>().ReadOnlyQuery().ToListAsync(cancellationToken), cancellationToken);

    /// <summary>Creates or updates a Channel's row from what the Host says of its Package.</summary>
    /// <param name="package">The Package record.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What changed.</returns>
    public Task<ChannelUpsert> UpsertChannelAsync(Package package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        return WriteAsync(async context =>
        {
            EntitySet<ChannelRow> channels = context.Set<ChannelRow>();
            bool followed = package.State == PackageState.Followed;
            ChannelRow? row = await channels.FindAsync(RowBytes.Of(package.PackageId.Value), cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                row = new ChannelRow { PackageId = RowBytes.Of(package.PackageId.Value), Name = package.Name, OwnerId = RowBytes.Of(package.OwnerId.Value), Followed = followed };
                channels.Add(row);
                return new ChannelUpsert(row, Created: true, Renamed: false, FollowChanged: true);
            }

            bool renamed = row.Name != package.Name;
            bool followChanged = row.Followed != followed;
            row.Name = package.Name;
            row.Followed = followed;
            if (followChanged && !followed)
                row.Indexed = false;
            return new ChannelUpsert(row, Created: false, renamed, followChanged);
        }, cancellationToken);
    }

    /// <summary>Records whether a Channel's whole history held here is indexed.</summary>
    /// <param name="package">The Channel.</param>
    /// <param name="indexed">Whether it is.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The row after it, or <see langword="null"/> when the Channel is unknown.</returns>
    public Task<ChannelRow?> MarkIndexedAsync(PackageId package, bool indexed, CancellationToken cancellationToken) => WriteAsync(async context =>
    {
        ChannelRow? row = await context.Set<ChannelRow>().FindAsync(RowBytes.Of(package.Value), cancellationToken).ConfigureAwait(false);
        if (row is not null)
            row.Indexed = indexed;
        return row;
    }, cancellationToken);

    /// <summary>Removes a Channel and every row of it.</summary>
    /// <param name="package">The Channel.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The Channel's last row, or <see langword="null"/> when it was unknown.</returns>
    public Task<ChannelRow?> DropChannelAsync(PackageId package, CancellationToken cancellationToken) => WriteAsync(async context =>
    {
        byte[] id = RowBytes.Of(package.Value);
        foreach (MessageRow row in await context.Set<MessageRow>().Query().Where(MessageRow.Columns.Package == id).ToListAsync(cancellationToken).ConfigureAwait(false))
            context.Set<MessageRow>().Remove(row);
        foreach (AttachmentRow row in await context.Set<AttachmentRow>().Query().Where(AttachmentRow.Columns.Package == id).ToListAsync(cancellationToken).ConfigureAwait(false))
            context.Set<AttachmentRow>().Remove(row);
        foreach (RedactionRow row in await context.Set<RedactionRow>().Query().Where(RedactionRow.Columns.Package == id).ToListAsync(cancellationToken).ConfigureAwait(false))
            context.Set<RedactionRow>().Remove(row);
        ChannelRow? channel = await context.Set<ChannelRow>().FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (channel is not null)
            context.Set<ChannelRow>().Remove(channel);
        return channel;
    }, cancellationToken);

    /// <summary>
    /// Takes in Entries of the chat kinds in one transaction (CHAT-SERVICE.md §11, <see cref="IndexBatch"/>): a page of the
    /// runtime's catch-up, or one Entry. An Entry indexed before, here or earlier in the batch, changes nothing; rows, counts
    /// and the meta row's store sequence are committed together, lazily durable (a crash loses only the latest commits, and
    /// the runtime's later cursor commit with them, so those Entries come again).
    /// </summary>
    /// <param name="entries">The Entries, in store order; their Channels' rows exist.</param>
    /// <param name="owners">Each Channel's owner and the devices known, for SPEC.md §9.4; an Entry of a Channel not in it is skipped.</param>
    /// <param name="advance">Whether the Entries came from the Host's stream, so the meta row's store sequence moves to the last.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What changed, one result per Entry.</returns>
    public Task<IReadOnlyList<IndexResult>> IndexManyAsync(IReadOnlyList<Entry> entries, IReadOnlyDictionary<PackageId, ChannelOwner> owners, bool advance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(owners);
        return WriteAsync<IReadOnlyList<IndexResult>>(async context =>
        {
            var batch = new IndexBatch(context);
            var results = new List<IndexResult>(entries.Count);
            foreach (Entry entry in entries)
                results.Add(owners.TryGetValue(entry.PackageId, out ChannelOwner? owner) ? await batch.TakeInAsync(entry, owner, cancellationToken).ConfigureAwait(false) : IndexResult.Nothing);
            if (advance && entries.Count > 0)
            {
                MetaRow meta = await MetaAsync(context, cancellationToken).ConfigureAwait(false);
                meta.LastSeq = Math.Max(meta.LastSeq, entries.Max(e => e.StoreSeq));
            }

            return results;
        }, Durability.Lazy, cancellationToken);
    }

    /// <summary>Takes in one Entry; see <see cref="IndexManyAsync"/>.</summary>
    /// <param name="entry">The Entry; its Channel's row exists.</param>
    /// <param name="owner">The Channel's owner and the devices known, for SPEC.md §9.4.</param>
    /// <param name="advance">Whether the Entry came from the Host's stream.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What changed.</returns>
    public async Task<IndexResult> IndexAsync(Entry entry, ChannelOwner owner, bool advance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(owner);
        return (await IndexManyAsync([entry], new Dictionary<PackageId, ChannelOwner> { [entry.PackageId] = owner }, advance, cancellationToken).ConfigureAwait(false))[0];
    }


    /// <summary>One message's row.</summary>
    /// <param name="hash">The message's Entry hash.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The row, or <see langword="null"/>.</returns>
    public Task<MessageRow?> MessageAsync(ObjectHash hash, CancellationToken cancellationToken) =>
        ReadAsync(context => context.Set<MessageRow>().FindAsync(RowBytes.Of(hash.Value), cancellationToken), cancellationToken);

    /// <summary>
    /// The messages of a Channel after a position in the total order (or before it, newest first), at most
    /// <paramref name="count"/>: read through the clock index, the last clock read whole and put in order in memory.
    /// </summary>
    /// <param name="package">The Channel.</param>
    /// <param name="after">The previous page's last key, or <see langword="null"/> for the first page.</param>
    /// <param name="reverse">Newest first.</param>
    /// <param name="count">At most this many; at least 1.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The rows in the page's order.</returns>
    public Task<List<MessageRow>> PageAsync(PackageId package, EntryKey? after, bool reverse, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return ReadAsync(context => reverse ? BackwardAsync(context, package, after, count, cancellationToken) : ForwardAsync(context, package, after, count, cancellationToken), cancellationToken);
    }

    /// <summary>A message and the messages it answers, up the <c>replyTo</c> chain as far as they are indexed.</summary>
    /// <param name="message">The message.</param>
    /// <param name="maxDepth">The most steps up.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The rows of the chain, the message first; empty when it is not indexed.</returns>
    public Task<List<MessageRow>> ChainAsync(ObjectHash message, int maxDepth, CancellationToken cancellationToken) => ReadAsync(async context =>
    {
        var chain = new List<MessageRow>();
        byte[]? next = RowBytes.Of(message.Value);
        for (int step = 0; step <= maxDepth && next is not null; step++)
        {
            MessageRow? row = await context.Set<MessageRow>().FindAsync(next, cancellationToken).ConfigureAwait(false);
            if (row is null || chain.Any(r => r.Hash.AsSpan().SequenceEqual(row.Hash)))
                break;
            chain.Add(row);
            next = row.ReplyTo;
        }

        return chain;
    }, cancellationToken);

    /// <summary>Every message under a root, at any depth, through the <c>replyTo</c> index.</summary>
    /// <param name="root">The root.</param>
    /// <param name="cap">The most rows read.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The rows, unordered, the root excluded.</returns>
    public Task<List<MessageRow>> DescendantsAsync(ObjectHash root, int cap, CancellationToken cancellationToken) => ReadAsync(async context =>
    {
        var found = new List<MessageRow>();
        var seen = new HashSet<ObjectHash> { root };
        var pending = new Queue<ObjectHash>([root]);
        while (found.Count < cap && pending.TryDequeue(out ObjectHash parent))
        {
            List<MessageRow> children = await context.Set<MessageRow>().ReadOnlyQuery()
                .Where(MessageRow.Columns.ReplyTo == RowBytes.Of(parent.Value))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (MessageRow child in children)
            {
                var hash = new ObjectHash(RowBytes.Hash(child.Hash));
                if (seen.Add(hash))
                {
                    found.Add(child);
                    pending.Enqueue(hash);
                }
            }
        }

        return found;
    }, cancellationToken);

    /// <summary>Records the objects an <c>attach</c> put for an attachment, to unpin once a message names it.</summary>
    /// <param name="root">The attachment: a ChunkList or root Tree.</param>
    /// <param name="objects">Every object put for it.</param>
    /// <param name="startedMs">When the job started, in milliseconds since the Unix epoch.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is committed.</returns>
    public Task AddPendingAsync(ObjectHash root, IReadOnlyList<ObjectHash> objects, long startedMs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(objects);
        return WriteAsync(async context =>
        {
            EntitySet<PendingRow> pending = context.Set<PendingRow>();
            foreach (ObjectHash hash in objects.Distinct())
            {
                PendingRow? row = await pending.FindAsync(RowBytes.Of(hash.Value), cancellationToken).ConfigureAwait(false);
                if (row is null)
                    pending.Add(new PendingRow { Hash = RowBytes.Of(hash.Value), Root = RowBytes.Of(root.Value), StartedMs = startedMs });
                else
                    row.Root = RowBytes.Of(root.Value);
            }
        }, cancellationToken);
    }

    /// <summary>Takes the pending objects of some attachments: returns them and forgets them.</summary>
    /// <param name="roots">The attachments.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The objects to unpin, ascending.</returns>
    public Task<IReadOnlyList<ObjectHash>> TakePendingAsync(IReadOnlyList<ObjectHash> roots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roots);
        return WriteAsync<IReadOnlyList<ObjectHash>>(async context =>
        {
            var taken = new List<ObjectHash>();
            foreach (ObjectHash root in roots.Distinct())
            {
                foreach (PendingRow row in await context.Set<PendingRow>().Query().Where(PendingRow.Columns.Root == RowBytes.Of(root.Value)).ToListAsync(cancellationToken).ConfigureAwait(false))
                {
                    taken.Add(new ObjectHash(RowBytes.Hash(row.Hash)));
                    context.Set<PendingRow>().Remove(row);
                }
            }

            return [.. taken.Order()];
        }, cancellationToken);
    }

    /// <summary>Takes every pending object: returns them and forgets them, at the service's start.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The objects to unpin, ascending.</returns>
    public Task<IReadOnlyList<ObjectHash>> TakeAllPendingAsync(CancellationToken cancellationToken) => WriteAsync<IReadOnlyList<ObjectHash>>(async context =>
    {
        var taken = new List<ObjectHash>();
        foreach (PendingRow row in await context.Set<PendingRow>().Query().ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            taken.Add(new ObjectHash(RowBytes.Hash(row.Hash)));
            context.Set<PendingRow>().Remove(row);
        }

        return [.. taken.Order()];
    }, cancellationToken);

    /// <summary>A message row's position in the total order.</summary>
    /// <param name="row">The row.</param>
    /// <returns>Its key.</returns>
    public static EntryKey KeyOf(MessageRow row) =>
        new(row.Clock, new DeviceId(RowBytes.Hash(row.Device)), new ObjectHash(RowBytes.Hash(row.Hash)));

    /// <inheritdoc />
    public override string ToString() => $"chat index on {database}";

    private static async Task<MetaRow> MetaAsync(LdbContext context, CancellationToken cancellationToken)
    {
        MetaRow? meta = await context.Set<MetaRow>().FindAsync(MetaName, cancellationToken).ConfigureAwait(false);
        if (meta is not null)
            return meta;
        meta = new MetaRow { Name = MetaName, SchemaVersion = CatchUpPlan.SchemaVersion };
        context.Set<MetaRow>().Add(meta);
        return meta;
    }

    private static async Task RemoveAllAsync<T>(EntitySet<T> set, CancellationToken cancellationToken)
        where T : class, IEntity<T>
    {
        foreach (T row in await set.Query().ToListAsync(cancellationToken).ConfigureAwait(false))
            set.Remove(row);
    }

    private static async Task<List<MessageRow>> ForwardAsync(LdbContext context, PackageId package, EntryKey? after, int count, CancellationToken cancellationToken)
    {
        byte[] id = RowBytes.Of(package.Value);
        var page = new List<MessageRow>(count);
        ulong fromClock = after?.Clock ?? 0;
        while (page.Count < count)
        {
            int remaining = count - page.Count;
            List<MessageRow> candidates = await context.Set<MessageRow>().ReadOnlyQuery()
                .Where(MessageRow.Columns.Package == id & MessageRow.Columns.Clock >= fromClock)
                .OrderBy(MessageRow.Columns.Clock)
                .Take(remaining + 1)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0)
                break;

            // Rows of one clock come in row order, so the last clock read may be cut: read it whole and order it.
            ulong lastClock = candidates[^1].Clock;
            List<MessageRow> lastClockRows = await context.Set<MessageRow>().ReadOnlyQuery()
                .Where(MessageRow.Columns.Package == id & MessageRow.Columns.Clock == lastClock)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            page.AddRange(candidates.Where(r => r.Clock < lastClock).Concat(lastClockRows)
                .Where(r => Timeline.Later(KeyOf(r), after, reverse: false))
                .OrderBy(KeyOf)
                .Take(remaining));

            if (candidates.Count <= remaining || lastClock == ulong.MaxValue)
                break;
            fromClock = lastClock + 1;
        }

        return page;
    }

    private static async Task<List<MessageRow>> BackwardAsync(LdbContext context, PackageId package, EntryKey? before, int count, CancellationToken cancellationToken)
    {
        byte[] id = RowBytes.Of(package.Value);
        var page = new List<MessageRow>(count);
        ulong toClock = before?.Clock ?? ulong.MaxValue;
        while (page.Count < count)
        {
            int remaining = count - page.Count;
            List<MessageRow> candidates = await context.Set<MessageRow>().ReadOnlyQuery()
                .Where(MessageRow.Columns.Package == id & MessageRow.Columns.Clock <= toClock)
                .OrderByDescending(MessageRow.Columns.Clock)
                .Take(remaining + 1)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0)
                break;

            ulong lastClock = candidates[^1].Clock;
            List<MessageRow> lastClockRows = await context.Set<MessageRow>().ReadOnlyQuery()
                .Where(MessageRow.Columns.Package == id & MessageRow.Columns.Clock == lastClock)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            page.AddRange(candidates.Where(r => r.Clock > lastClock).Concat(lastClockRows)
                .Where(r => Timeline.Later(KeyOf(r), before, reverse: true))
                .OrderByDescending(KeyOf)
                .Take(remaining));

            if (candidates.Count <= remaining || lastClock == 0)
                break;
            toClock = lastClock - 1;
        }

        return page;
    }

    private async Task<T> ReadAsync<T>(Func<LdbContext, Task<T>> read, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using LdbContext context = await LdbContext.OpenAsync(database, cancellationToken).ConfigureAwait(false);
            return await read(context).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private Task WriteAsync(Func<LdbContext, Task> write, CancellationToken cancellationToken) =>
        WriteAsync<bool>(async context =>
        {
            await write(context).ConfigureAwait(false);
            return true;
        }, Durability.Full, cancellationToken);

    private Task<T> WriteAsync<T>(Func<LdbContext, Task<T>> write, CancellationToken cancellationToken) =>
        WriteAsync(write, Durability.Full, cancellationToken);

    // Rows built from Entries are a cache rebuilt by store sequence: their commits are lazily durable (a crash loses only
    // the latest commits, in order, and the runtime's later cursor commit with them, so the Entries come again).
    private Task WriteLazyAsync(Func<LdbContext, Task> write, CancellationToken cancellationToken) =>
        WriteAsync<bool>(async context =>
        {
            await write(context).ConfigureAwait(false);
            return true;
        }, Durability.Lazy, cancellationToken);

    private async Task<T> WriteAsync<T>(Func<LdbContext, Task<T>> write, Durability durability, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using LdbContext context = await LdbContext.OpenAsync(database, cancellationToken).ConfigureAwait(false);
            T result = await write(context).ConfigureAwait(false);
            await context.SaveChangesAsync(TransactionOptions.Default with { Durability = durability }, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
}
