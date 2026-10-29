using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// The pins of SPEC.md §11.2 inside one batch: adding and removing them with the object's pin count and the store's
/// pinned bytes, pinning a closure through Trees and ChunkLists, and passing <c>Policy</c> and <c>Outbound</c> pins on
/// to an object that arrives after what references it.
/// </summary>
/// <remarks>
/// The invariant it keeps: an object that holds a <c>Policy</c> or <c>Outbound</c> pin passes it to every stored object
/// it references that the retention row keeps. A closure therefore stops where the pin is already present, and an
/// object stored later takes the pin from its referrers when it arrives.
/// </remarks>
internal sealed class PinBook
{
    private readonly BatchTables tables;
    private readonly WriterState state;
    private readonly StoreOptions options;
    private readonly ChangeSetBuilder changes;
    private readonly long now;

    /// <summary>The pins of one batch.</summary>
    /// <param name="tables">The batch's tables.</param>
    /// <param name="state">The writer's state, whose pinned bytes the pins keep.</param>
    /// <param name="options">The device class and budget.</param>
    /// <param name="changes">Where pin changes are recorded.</param>
    /// <param name="now">The batch's time, in milliseconds since the Unix epoch.</param>
    public PinBook(BatchTables tables, WriterState state, StoreOptions options, ChangeSetBuilder changes, long now)
    {
        this.tables = tables;
        this.state = state;
        this.options = options;
        this.changes = changes;
        this.now = now;
    }

    /// <summary>Every pin, in any order, whatever the reason and source: for every object kept.</summary>
    private static Func<ObjectTag, ObjectTag, bool> KeepAll { get; } = static (_, _) => true;

    /// <summary>Pins a stored object; nothing happens for an object that is not stored or already holds the pin.</summary>
    /// <param name="hash">The object.</param>
    /// <param name="reason">Why.</param>
    /// <param name="source">Who for.</param>
    /// <returns><see langword="true"/> when the pin was added now.</returns>
    public async Task<bool> PinAsync(ObjectHash hash, PinReason reason, Hash32 source)
    {
        ObjectRow? row = await tables.Objects.FindAsync(RowKeys.Of(hash.Value), CancellationToken.None).ConfigureAwait(false);
        return row is not null && await AddAsync(row, hash, reason, source).ConfigureAwait(false);
    }

    /// <summary>Removes one pin.</summary>
    /// <param name="hash">The object.</param>
    /// <param name="reason">Why it was pinned.</param>
    /// <param name="source">Who for.</param>
    /// <returns><see langword="true"/> when the pin existed.</returns>
    public async Task<bool> UnpinAsync(ObjectHash hash, PinReason reason, Hash32 source)
    {
        if (!await tables.Pins.DeleteAsync(RowKeys.Pin(hash, reason, source), CancellationToken.None).ConfigureAwait(false))
            return false;

        ObjectRow? row = await tables.Objects.FindAsync(RowKeys.Of(hash.Value), CancellationToken.None).ConfigureAwait(false);
        if (row is not null)
        {
            row.PinCount--;
            if (row.PinCount == 0)
                state.PinnedBytes -= row.Size;
            await tables.Objects.PutAsync(row, CancellationToken.None).ConfigureAwait(false);
        }

        changes.Pins.Add(new PinChange(hash, reason, source, Added: false));
        return true;
    }

    /// <summary>Removes every pin of a reason and source (LOCAL-SPEC.md §6.5 <c>ReleasePins</c>; unfollowing; a new policy).</summary>
    /// <param name="reason">The reason.</param>
    /// <param name="source">The source.</param>
    /// <returns>How many pins were removed.</returns>
    public async Task<int> ReleaseAsync(PinReason reason, Hash32 source)
    {
        List<PinRow> held = await tables.Pins.Query()
            .Where(PinRow.Columns.Source == RowKeys.Of(source) & PinRow.Columns.Reason == (byte)reason)
            .ToListAsync(CancellationToken.None).ConfigureAwait(false);
        int removed = 0;
        foreach (PinRow pin in held)
            removed += await UnpinAsync(new ObjectHash(RowKeys.Hash(pin.Hash)), reason, source).ConfigureAwait(false) ? 1 : 0;
        return removed;
    }

    /// <summary>An object was sent to a peer in answer to a <c>Want</c>: its <c>Outbound</c> pins go (SPEC.md §11.2).</summary>
    /// <param name="hash">The object.</param>
    /// <returns>How many pins were removed.</returns>
    public async Task<int> ReleaseOutboundAsync(ObjectHash hash)
    {
        List<PinRow> held = await tables.Pins.Query()
            .Where(PinRow.Columns.Hash == RowKeys.Of(hash.Value) & PinRow.Columns.Reason == (byte)PinReason.Outbound)
            .ToListAsync(CancellationToken.None).ConfigureAwait(false);
        int removed = 0;
        foreach (PinRow pin in held)
            removed += await UnpinAsync(hash, PinReason.Outbound, RowKeys.Hash(pin.Source)).ConfigureAwait(false) ? 1 : 0;
        return removed;
    }

    /// <summary>
    /// Pins <paramref name="root"/> and, through Trees and ChunkLists, every stored object below it that
    /// <paramref name="keeps"/> keeps; stops where the pin is already present.
    /// </summary>
    /// <param name="root">The object an Entry, Tree or ChunkList references.</param>
    /// <param name="referrer">The tag of what references the root.</param>
    /// <param name="reason">Why.</param>
    /// <param name="source">Who for.</param>
    /// <param name="keeps">Whether an object of a tag is kept below a referrer of a tag.</param>
    /// <returns>A task that completes when the closure is pinned.</returns>
    public async Task PinClosureAsync(ObjectHash root, ObjectTag referrer, PinReason reason, Hash32 source, Func<ObjectTag, ObjectTag, bool> keeps)
    {
        var pending = new Stack<(ObjectHash Hash, ObjectTag Referrer)>();
        pending.Push((root, referrer));
        while (pending.TryPop(out (ObjectHash Hash, ObjectTag Referrer) next))
        {
            ObjectRow? row = await tables.Objects.FindAsync(RowKeys.Of(next.Hash.Value), CancellationToken.None).ConfigureAwait(false);
            if (row is null || !keeps(next.Referrer, (ObjectTag)row.Tag) || !await AddAsync(row, next.Hash, reason, source).ConfigureAwait(false))
                continue;

            if ((ObjectTag)row.Tag is ObjectTag.Tree or ObjectTag.ChunkList)
            {
                foreach (ObjectHash child in await ChildrenAsync(next.Hash).ConfigureAwait(false))
                    pending.Push((child, (ObjectTag)row.Tag));
            }
        }
    }

    /// <summary>
    /// Applies a followed Package's retention row to the objects one of its Entries references (SPEC.md §11.2). An Entry
    /// that is not the newest <c>Version</c> of an Archive under the latest-only row keeps nothing.
    /// </summary>
    /// <param name="package">The followed Package with its Manifest.</param>
    /// <param name="references">The Entry's payload references.</param>
    /// <param name="newestVersion">Whether the Entry is the Package's newest stored <c>Version</c>.</param>
    /// <param name="kind">The Entry's kind.</param>
    /// <returns>A task that completes when the pins are set.</returns>
    public async Task ApplyPolicyAsync(PackageTrust package, IReadOnlyList<ObjectHash> references, EntryKind kind, bool newestVersion)
    {
        (RetentionRow row, PackageKind packageKind) = RowOf(package);
        if (row == RetentionRow.LatestOnly && packageKind == PackageKind.Archive && kind == EntryKind.Version)
        {
            if (!newestVersion)
                return;
            await ReleaseAsync(PinReason.Policy, package.Id.Value).ConfigureAwait(false); // only the newest Version is kept
        }

        foreach (ObjectHash reference in references)
            await PinClosureAsync(reference, ObjectTag.Entry, PinReason.Policy, package.Id.Value, Keeps(row, packageKind)).ConfigureAwait(false);
    }

    /// <summary>Gives a newly stored object the <c>Policy</c> and <c>Outbound</c> pins its referrers pass on.</summary>
    /// <param name="hash">The object stored now.</param>
    /// <param name="trust">The writer's trust.</param>
    /// <returns>A task that completes when the pins are set.</returns>
    public async Task InheritAsync(ObjectHash hash, TrustBuilder trust)
    {
        List<ReferenceRow> referrers = await tables.References.Query()
            .Where(ReferenceRow.Columns.Target == RowKeys.Of(hash.Value))
            .ToListAsync(CancellationToken.None).ConfigureAwait(false);
        foreach (ReferenceRow up in referrers)
        {
            byte[] referrerKey = up.Referrer;
            ObjectRow? referrer = await tables.Objects.FindAsync(referrerKey, CancellationToken.None).ConfigureAwait(false);
            if (referrer is null)
                continue;

            if ((ObjectTag)referrer.Tag == ObjectTag.Entry)
                await InheritFromEntryAsync(hash, referrerKey, trust).ConfigureAwait(false);
            else
                await InheritFromObjectAsync(hash, referrerKey, (ObjectTag)referrer.Tag, trust).ConfigureAwait(false);
        }
    }

    /// <summary>The objects a stored object references, as the index recorded them.</summary>
    /// <param name="referrer">The referencing object.</param>
    /// <returns>The referenced objects, stored or not.</returns>
    public async Task<IReadOnlyList<ObjectHash>> ChildrenAsync(ObjectHash referrer)
    {
        List<ReferenceRow> rows = await tables.References.Query()
            .Where(ReferenceRow.Columns.Referrer == RowKeys.Of(referrer.Value))
            .ToListAsync(CancellationToken.None).ConfigureAwait(false);
        return [.. rows.Select(r => new ObjectHash(RowKeys.Hash(r.Target)))];
    }

    private static Func<ObjectTag, ObjectTag, bool> Keeps(RetentionRow row, PackageKind kind) =>
        (referrer, target) => RetentionRule.Keeps(row, kind, referrer, target);

    private (RetentionRow Row, PackageKind Kind) RowOf(PackageTrust package)
    {
        ManifestContent manifest = package.Manifest!.Content;
        return (RetentionRule.Effective(options.DeviceClass, package.Policy, manifest.Retention), manifest.Kind);
    }

    private async Task InheritFromEntryAsync(ObjectHash hash, byte[] entryKey, TrustBuilder trust)
    {
        EntryRow? entry = await tables.Entries.FindAsync(entryKey, CancellationToken.None).ConfigureAwait(false);
        if (entry is null || !trust.TryGetPackage(new PackageId(RowKeys.Hash(entry.Package)), out PackageTrust? package) || !package.IsFollowed || package.Manifest is null)
            return;

        (RetentionRow row, PackageKind kind) = RowOf(package);
        bool kept = true;
        if (row == RetentionRow.LatestOnly && kind == PackageKind.Archive && entry.Kind == EntryKind.Version)
        {
            PackageRow? packageRow = await tables.Packages.FindAsync(entry.Package, CancellationToken.None).ConfigureAwait(false);
            kept = packageRow?.NewestVersion is { } newest && newest.AsSpan().SequenceEqual(entryKey);
        }

        if (kept)
            await PinClosureAsync(hash, ObjectTag.Entry, PinReason.Policy, package.Id.Value, Keeps(row, kind)).ConfigureAwait(false);
        if (trust.Self is { } self && new DeviceId(RowKeys.Hash(entry.Device)) == self.Device)
            await PinClosureAsync(hash, ObjectTag.Entry, PinReason.Outbound, package.Id.Value, KeepAll).ConfigureAwait(false);
    }

    private async Task InheritFromObjectAsync(ObjectHash hash, byte[] referrerKey, ObjectTag referrerTag, TrustBuilder trust)
    {
        List<PinRow> pins = await tables.Pins.Query()
            .Where(PinRow.Columns.Hash == referrerKey)
            .ToListAsync(CancellationToken.None).ConfigureAwait(false);
        foreach (PinRow pin in pins.Where(p => p.Reason is PinReason.Policy or PinReason.Outbound))
        {
            Hash32 source = RowKeys.Hash(pin.Source);
            Func<ObjectTag, ObjectTag, bool> keeps = KeepAll;
            if (pin.Reason == PinReason.Policy)
            {
                if (!trust.TryGetPackage(new PackageId(source), out PackageTrust? package) || package.Manifest is null)
                    continue;
                (RetentionRow row, PackageKind kind) = RowOf(package);
                keeps = Keeps(row, kind);
            }

            await PinClosureAsync(hash, referrerTag, pin.Reason, source, keeps).ConfigureAwait(false);
        }
    }

    private async Task<bool> AddAsync(ObjectRow row, ObjectHash hash, PinReason reason, Hash32 source)
    {
        byte[] key = RowKeys.Pin(hash, reason, source);
        if (await tables.Pins.FindAsync(key, CancellationToken.None).ConfigureAwait(false) is not null)
            return false;

        await tables.Pins.PutAsync(new PinRow { Key = key, Hash = row.Hash, Reason = reason, Source = RowKeys.Of(source), SinceMs = now }, CancellationToken.None).ConfigureAwait(false);
        if (row.PinCount == 0)
            state.PinnedBytes += row.Size;
        row.PinCount++;
        await tables.Objects.PutAsync(row, CancellationToken.None).ConfigureAwait(false);
        changes.Pins.Add(new PinChange(hash, reason, source, Added: true));
        return true;
    }
}
