using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Chat;

/// <summary>
/// Entries taken in within one transaction of the index (CHAT-SERVICE.md §11): a page of the runtime's catch-up, or one
/// Entry. It remembers every row it read or added, so an Entry, its redaction and its Channel's counts meet the same row
/// whether it was read from the database or added a moment ago in this batch; nothing is visible to others before the
/// commit.
/// </summary>
internal sealed class IndexBatch
{
    private readonly LdbContext context;
    private readonly Dictionary<ObjectHash, MessageRow?> messages = [];
    private readonly HashSet<ObjectHash> redactions = [];
    private readonly Dictionary<ObjectHash, List<RedactionRow>> waiting = [];
    private readonly Dictionary<PackageId, ChannelRow?> channels = [];

    /// <summary>A batch on a unit of work.</summary>
    /// <param name="context">The unit of work; the caller saves it.</param>
    public IndexBatch(LdbContext context) => this.context = context;

    /// <summary>
    /// Takes in one Entry: a redaction (any Entry whose envelope's <c>redacts</c> is not zero) is kept and applied to its
    /// target when SPEC.md §9.4 honours it; a Message is indexed with what its payload says and whether it is broken, and
    /// every redaction kept for it is applied; anything else, an Entry indexed before, or one of an unknown Channel, is skipped.
    /// </summary>
    /// <param name="entry">The Entry.</param>
    /// <param name="owner">Its Channel's owner and the devices known.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>What changed.</returns>
    public async Task<IndexResult> TakeInAsync(Entry entry, ChannelOwner owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(owner);
        if (await KnownAsync(entry, cancellationToken).ConfigureAwait(false))
            return IndexResult.Nothing;
        if (await ChannelAsync(entry.PackageId, cancellationToken).ConfigureAwait(false) is not { } channel)
            return IndexResult.Nothing;
        if (!entry.Redacts.IsZero)
            return await RedactAsync(entry, channel, owner, cancellationToken).ConfigureAwait(false);
        return entry.Kind == EntryKind.Message
            ? await IndexMessageAsync(entry, channel, owner, cancellationToken).ConfigureAwait(false)
            : IndexResult.Nothing;
    }

    private static EntryFacts FactsOf(MessageRow row) => new(new PackageId(RowBytes.Hash(row.Package)), default, ChatIndex.KeyOf(row));

    private static EntryFacts FactsOf(RedactionRow row) =>
        new(new PackageId(RowBytes.Hash(row.Package)), new ObjectHash(RowBytes.Hash(row.Target)), new EntryKey(row.Clock, new DeviceId(RowBytes.Hash(row.Device)), new ObjectHash(RowBytes.Hash(row.Hash))));

    // An Entry is in one table: the redactions when its envelope redacts, else the messages.
    private async Task<bool> KnownAsync(Entry entry, CancellationToken cancellationToken)
    {
        if (!entry.Redacts.IsZero)
        {
            return redactions.Contains(entry.Hash)
                || await context.Set<RedactionRow>().FindAsync(RowBytes.Of(entry.Hash.Value), cancellationToken).ConfigureAwait(false) is not null;
        }

        return await MessageAsync(entry.Hash, cancellationToken).ConfigureAwait(false) is not null;
    }

    private async Task<MessageRow?> MessageAsync(ObjectHash hash, CancellationToken cancellationToken)
    {
        if (messages.TryGetValue(hash, out MessageRow? known))
            return known;
        MessageRow? read = await context.Set<MessageRow>().FindAsync(RowBytes.Of(hash.Value), cancellationToken).ConfigureAwait(false);
        messages[hash] = read;
        return read;
    }

    private async Task<ChannelRow?> ChannelAsync(PackageId package, CancellationToken cancellationToken)
    {
        if (channels.TryGetValue(package, out ChannelRow? known))
            return known;
        ChannelRow? read = await context.Set<ChannelRow>().FindAsync(RowBytes.Of(package.Value), cancellationToken).ConfigureAwait(false);
        channels[package] = read;
        return read;
    }

    private async Task<IndexResult> RedactAsync(Entry entry, ChannelRow channel, ChannelOwner owner, CancellationToken cancellationToken)
    {
        var redaction = new RedactionRow
        {
            Hash = RowBytes.Of(entry.Hash.Value),
            Target = RowBytes.Of(entry.Redacts.Value),
            Package = RowBytes.Of(entry.PackageId.Value),
            Device = RowBytes.Of(entry.DeviceId.Value),
            Clock = entry.Clock,
            StoreSeq = entry.StoreSeq,
        };
        context.Set<RedactionRow>().Add(redaction);
        redactions.Add(entry.Hash);
        if (!waiting.TryGetValue(entry.Redacts, out List<RedactionRow>? kept))
            waiting[entry.Redacts] = kept = [];
        kept.Add(redaction);

        MessageRow? target = await MessageAsync(entry.Redacts, cancellationToken).ConfigureAwait(false);
        RedactionOutcome outcome = RedactionApplication.Decide(FactsOf(redaction), target is null ? null : FactsOf(target), owner.HasDevice(entry.DeviceId));
        if (outcome != RedactionOutcome.Applied || target!.Redacted)
            return new IndexResult(IndexChange.Kept, null, default);
        target.Redacted = true;
        target.RedactedBy = redaction.Device;
        channel.Withdrawn++;
        return new IndexResult(IndexChange.Withdrew, target, entry.DeviceId);
    }

    private async Task<IndexResult> IndexMessageAsync(Entry entry, ChannelRow channel, ChannelOwner owner, CancellationToken cancellationToken)
    {
        byte[] hash = RowBytes.Of(entry.Hash.Value);
        MessageCheck check = PayloadCheck.OfMessage(entry.Kind, entry.Payload, entry.References, entry.Redacts);
        var row = new MessageRow
        {
            Hash = hash,
            Package = RowBytes.Of(entry.PackageId.Value),
            Clock = entry.Clock,
            Device = RowBytes.Of(entry.DeviceId.Value),
            StoredMs = (long)Math.Min(entry.Stored, long.MaxValue),
            StoreSeq = entry.StoreSeq,
            Text = check.Payload?.Text ?? string.Empty,
            Attachments = AttachmentsBlob.Of(check.Payload?.Attachments ?? []),
            AttachmentCount = check.Payload?.Attachments.Count ?? 0,
            ReplyTo = check.Payload is { ReplyTo.IsZero: false } payload ? RowBytes.Of(payload.ReplyTo.Value) : null,
            Broken = check.Broken,
        };

        // SPEC.md §9.4 in the other order: a redaction kept before its target, stored or in this batch, applies now.
        List<RedactionRow> before = await context.Set<RedactionRow>().ReadOnlyQuery().Where(RedactionRow.Columns.Target == hash).ToListAsync(cancellationToken).ConfigureAwait(false);
        IEnumerable<RedactionRow> kept = waiting.TryGetValue(entry.Hash, out List<RedactionRow>? inBatch) ? before.Concat(inBatch) : before;
        foreach (RedactionRow redaction in Timeline.Order(kept, r => FactsOf(r).Key, reverse: false))
        {
            if (RedactionApplication.Decide(FactsOf(redaction), FactsOf(row), owner.HasDevice(new DeviceId(RowBytes.Hash(redaction.Device)))) != RedactionOutcome.Applied)
                continue;
            row.Redacted = true;
            row.RedactedBy = redaction.Device;
            break;
        }

        context.Set<MessageRow>().Add(row);
        messages[entry.Hash] = row;
        foreach (Attachment attachment in check.Payload?.Attachments ?? [])
        {
            byte[] pair = RowBytes.Pair(attachment.Hash.Value, entry.Hash.Value);
            if (await context.Set<AttachmentRow>().FindAsync(pair, cancellationToken).ConfigureAwait(false) is null)
                context.Set<AttachmentRow>().Add(new AttachmentRow { Key = pair, Attachment = RowBytes.Of(attachment.Hash.Value), Package = row.Package, Name = attachment.Name, Size = attachment.Size });
        }

        channel.Messages++;
        if (row.Redacted)
            channel.Withdrawn++;
        EntryKey key = ChatIndex.KeyOf(row);
        if (channel.LastHash is null || key > new EntryKey(channel.LastClock, new DeviceId(RowBytes.Hash(channel.LastDevice)), new ObjectHash(RowBytes.Hash(channel.LastHash))))
        {
            channel.LastClock = row.Clock;
            channel.LastDevice = row.Device;
            channel.LastHash = row.Hash;
        }

        return new IndexResult(IndexChange.NewMessage, row, default);
    }
}
