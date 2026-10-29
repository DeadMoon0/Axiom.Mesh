using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// One batch of the store writer: applies up to 256 commands inside one write transaction, writing each new object's
/// file before its rows, and defers every completion until the transaction committed. Holds the writer's decisions:
/// which current object a Roster, Manifest or Grant replaces (SPEC.md §8.7–§8.9), the store sequence and Head of an
/// Entry (LOCAL-SPEC.md §5.6, SPEC.md §9.3), redaction (§9.4), pins (§11.2), eviction (§11.3).
/// </summary>
internal sealed class StoreBatch
{
    private const int PeerAddresses = 8;
    private const long ViolationWindowMs = Limits.ViolationWindowSeconds * 1000L;

    private readonly BatchTables tables;
    private readonly WriterState state;
    private readonly IObjectFiles files;
    private readonly StoreOptions options;
    private readonly Promises promises;
    private readonly long now;
    private readonly PinBook pins;
    private readonly HashSet<ObjectHash> touched = [];
    private readonly List<Action> completions = [];

    /// <summary>A batch over a write transaction's tables.</summary>
    /// <param name="tables">The tables.</param>
    /// <param name="state">The writer's state; changed in place, and reloaded by the writer when the batch fails.</param>
    /// <param name="files">The object files.</param>
    /// <param name="options">The device class and budget.</param>
    /// <param name="promises">The objects a peer's outstanding <c>Want</c> protects from eviction.</param>
    /// <param name="now">The batch's time, in milliseconds since the Unix epoch.</param>
    public StoreBatch(BatchTables tables, WriterState state, IObjectFiles files, StoreOptions options, Promises promises, long now)
    {
        this.tables = tables;
        this.state = state;
        this.files = files;
        this.options = options;
        this.promises = promises;
        this.now = now;
        pins = new PinBook(tables, state, options, Changes, now);
    }

    /// <summary>What the batch changed.</summary>
    public ChangeSetBuilder Changes { get; } = new();

    /// <summary>Applies one command; its completion waits for <see cref="Complete"/>.</summary>
    /// <param name="command">The command.</param>
    /// <returns>A task that completes when the command is applied to the transaction.</returns>
    public async Task ApplyAsync(StoreCommand command)
    {
        switch (command)
        {
            case AdmitCommand admit:
                if (await AdmitAsync(admit).ConfigureAwait(false) is { } admitted)
                    Defer(admit, admitted);
                break;
            case TouchCommand touch:
                touched.Add(touch.Hash);
                Defer(touch, true);
                break;
            case PinCommand pin:
                Defer(pin, await PinAsync(pin).ConfigureAwait(false));
                break;
            case PackageCommand package:
                Defer(package, await PackageAsync(package).ConfigureAwait(false));
                break;
            case IdentityCommand identity:
                Defer(identity, await IdentityAsync(identity.Identity).ConfigureAwait(false));
                break;
            case PairCommand pair:
                Defer(pair, await PairAsync(pair.UserKey).ConfigureAwait(false));
                break;
            case PeerCommand peer:
                await PeerAsync(peer).ConfigureAwait(false);
                Defer(peer, true);
                break;
            case EvictCommand evict:
                Defer(evict, await EvictAsync(evict.Hash).ConfigureAwait(false));
                break;
            case EvictUnderBudgetCommand underBudget:
                Defer(underBudget, await EvictUnderBudgetAsync().ConfigureAwait(false));
                break;
            case WithdrawGrantCommand withdraw:
                Defer(withdraw, Withdraw(withdraw));
                break;
            default:
                throw new InvalidOperationException($"The store writer has no handler for {command.GetType().Name}.");
        }
    }

    /// <summary>Writes what the batch accumulated: touches and the store's counters.</summary>
    /// <returns>A task that completes when written.</returns>
    public async Task FinishAsync()
    {
        foreach (ObjectHash hash in touched)
        {
            if (await tables.Objects.FindAsync(RowKeys.Of(hash.Value), CancellationToken.None).ConfigureAwait(false) is { } row)
            {
                row.LastUsedMs = now;
                await tables.Objects.PutAsync(row, CancellationToken.None).ConfigureAwait(false);
            }
        }

        await PutNumberAsync(MetaKeys.StoreSeq, state.StoreSeq).ConfigureAwait(false);
        await PutNumberAsync(MetaKeys.StoreObjects, (ulong)state.Objects).ConfigureAwait(false);
        await PutNumberAsync(MetaKeys.StoreBytes, (ulong)state.Bytes).ConfigureAwait(false);
        await PutNumberAsync(MetaKeys.StorePinnedBytes, (ulong)state.PinnedBytes).ConfigureAwait(false);
    }

    /// <summary>Completes every command applied; called once the transaction committed.</summary>
    public void Complete()
    {
        foreach (Action complete in completions)
            complete();
    }

    private void Defer<T>(StoreCommand<T> command, T result) => completions.Add(() => command.Complete(result));

    private Task PutNumberAsync(string name, ulong value) => tables.Meta.PutAsync(new MetaRow { Name = name, Number = value }, CancellationToken.None);

    // ---- Admission -------------------------------------------------------------------------------------------------

    private async Task<AdmitResult?> AdmitAsync(AdmitCommand command)
    {
        StorableObject storable = command.Object;
        if (await tables.Objects.FindAsync(RowKeys.Of(storable.Hash.Value), CancellationToken.None).ConfigureAwait(false) is { } existing)
        {
            existing.LastUsedMs = now;
            existing.EvictPending = false; // wanted again while a reader held it: it stays
            await tables.Objects.PutAsync(existing, CancellationToken.None).ConfigureAwait(false);
            return new AdmitResult(AdmitOutcome.AlreadyStored, 0);
        }

        return storable switch
        {
            VerifiedRoster roster => await AdmitRosterAsync(command, roster).ConfigureAwait(false),
            VerifiedManifest manifest => await AdmitManifestAsync(command, manifest).ConfigureAwait(false),
            VerifiedGrant grant => await AdmitGrantAsync(command, grant).ConfigureAwait(false),
            AdmittedEntry entry => await AdmitEntryAsync(command, entry).ConfigureAwait(false),
            _ => await AdmitUnsignedAsync(command).ConfigureAwait(false),
        };
    }

    private async Task<AdmitResult?> AdmitRosterAsync(AdmitCommand command, VerifiedRoster roster)
    {
        UserId user = roster.Content.User;
        if (!state.Trust.IsKnown(user))
            return new AdmitResult(AdmitOutcome.NotWanted, 0);
        if (state.Trust.TryGetUser(user, out UserTrust? held) && held.Roster is { } current && !CurrentObject.Supersedes(roster.Content.Stamp, current.Content.Stamp))
            return new AdmitResult(AdmitOutcome.Superseded, 0);
        if (Overflows(command))
            return new AdmitResult(AdmitOutcome.StoreFull, 0);
        if (!await WriteAsync(command).ConfigureAwait(false))
            return null;

        RosterApplication application = state.Trust.ApplyRoster(roster, out VerifiedRoster? previous)!;
        byte[] userKey = RowKeys.Of(user.Value);
        UserRow? row = await tables.Users.FindAsync(userKey, CancellationToken.None).ConfigureAwait(false);
        await tables.Users.PutAsync(new UserRow
        {
            User = userKey,
            UserKey = roster.Content.UserKey.Encoded.ToArray(),
            Paired = row?.Paired ?? false,
            Roster = RowKeys.Of(roster.Hash.Value),
            RosterSeq = roster.Content.Seq,
            SinceMs = row?.SinceMs ?? now,
        }, CancellationToken.None).ConfigureAwait(false);

        foreach (EffectiveDevice device in application.Devices)
            await PutDeviceAsync(user, device.Device.Id, device.Device.Key, device.Device.Name, listed: true, device.Removal, application.RemovedEver.Contains(device.Device.Id)).ConfigureAwait(false);
        foreach (DeviceId omitted in application.Omitted)
        {
            DeviceRow? gone = await tables.Devices.FindAsync(RowKeys.Of(user.Value, omitted.Value), CancellationToken.None).ConfigureAwait(false);
            if (gone is not null)
            {
                gone.Listed = false;
                gone.Removal = DeviceRemoval.RemovedBefore;
                gone.RemovedEver = true;
                await tables.Devices.PutAsync(gone, CancellationToken.None).ConfigureAwait(false);
            }
        }

        await StoreRowAsync(command, ObjectLinks.None).ConfigureAwait(false);
        await pins.PinAsync(roster.Hash, PinReason.Head, user.Value).ConfigureAwait(false);
        if (previous is not null)
            await pins.UnpinAsync(previous.Hash, PinReason.Head, user.Value).ConfigureAwait(false);
        Changes.Users.Add(user);
        Changes.TrustChanged = true;
        return new AdmitResult(AdmitOutcome.Stored, 0);
    }

    private async Task<AdmitResult?> AdmitManifestAsync(AdmitCommand command, VerifiedManifest manifest)
    {
        PackageId id = manifest.Content.Package;
        if (!state.Trust.TryGetPackage(id, out PackageTrust? package) || package.State == PackageState.None)
            return new AdmitResult(AdmitOutcome.NotWanted, 0);
        VerifiedManifest? current = package.Manifest;
        if (current is not null && !CurrentObject.Supersedes(manifest.Content.Stamp, current.Content.Stamp))
            return new AdmitResult(AdmitOutcome.Superseded, 0);
        if (current is not null && CurrentObject.ChangesKind(manifest.Content, current.Content))
            return new AdmitResult(AdmitOutcome.KindChanged, 0);
        if (package.IsFollowed && Overflows(command))
            return new AdmitResult(AdmitOutcome.StoreFull, 0);
        if (!await WriteAsync(command).ConfigureAwait(false))
            return null;

        state.Trust.SetManifest(manifest);
        PackageRow row = await PackageRowAsync(id).ConfigureAwait(false);
        row.Manifest = RowKeys.Of(manifest.Hash.Value);
        row.ManifestSeq = manifest.Content.Seq;
        await tables.Packages.PutAsync(row, CancellationToken.None).ConfigureAwait(false);
        await StoreRowAsync(command, ObjectLinks.None).ConfigureAwait(false);
        if (package.IsFollowed)
        {
            await pins.PinAsync(manifest.Hash, PinReason.Head, id.Value).ConfigureAwait(false);
            if (current is not null)
                await pins.UnpinAsync(current.Hash, PinReason.Head, id.Value).ConfigureAwait(false);
        }

        Changes.Package(new PackageChange(id, PackageChangeKind.Manifest, command.Origin));
        Changes.TrustChanged = true;
        return new AdmitResult(AdmitOutcome.Stored, 0);
    }

    private async Task<AdmitResult?> AdmitGrantAsync(AdmitCommand command, VerifiedGrant grant)
    {
        PackageId id = grant.Content.Package;
        UserId grantee = grant.Content.Grantee;
        if (!state.Trust.TryGetPackage(id, out PackageTrust? package) || !package.IsFollowed || package.Manifest is null)
            return new AdmitResult(AdmitOutcome.NotWanted, 0);
        package.Grants.TryGetValue(grantee, out VerifiedGrant? current);
        if (current is not null && !CurrentObject.Supersedes(grant.Content.Stamp, current.Content.Stamp))
            return new AdmitResult(AdmitOutcome.Superseded, 0);
        if (Overflows(command))
            return new AdmitResult(AdmitOutcome.StoreFull, 0);
        if (!await WriteAsync(command).ConfigureAwait(false))
            return null;

        state.Trust.SetGrant(grant);
        await tables.Grants.PutAsync(new GrantRow
        {
            Key = RowKeys.Of(id.Value, grantee.Value),
            Package = RowKeys.Of(id.Value),
            Grantee = RowKeys.Of(grantee.Value),
            Grant = RowKeys.Of(grant.Hash.Value),
            Rights = grant.Content.Rights,
            Seq = grant.Content.Seq,
        }, CancellationToken.None).ConfigureAwait(false);
        await StoreRowAsync(command, ObjectLinks.None).ConfigureAwait(false);
        await pins.PinAsync(grant.Hash, PinReason.Head, id.Value).ConfigureAwait(false);
        if (current is not null)
            await pins.UnpinAsync(current.Hash, PinReason.Head, id.Value).ConfigureAwait(false);

        Changes.Package(new PackageChange(id, PackageChangeKind.Grant, command.Origin));
        Changes.TrustChanged = true;
        return new AdmitResult(AdmitOutcome.Stored, 0);
    }

    private async Task<AdmitResult?> AdmitEntryAsync(AdmitCommand command, AdmittedEntry entry)
    {
        EntryContent content = entry.Content;
        PackageId id = content.Package;
        if (!state.Trust.TryGetPackage(id, out PackageTrust? package) || !package.IsFollowed || package.Manifest is null)
            return new AdmitResult(AdmitOutcome.NotWanted, 0);
        if (await HasParentFaultAsync(content).ConfigureAwait(false))
            return new AdmitResult(AdmitOutcome.Faulty, 0);
        if (Overflows(command))
            return new AdmitResult(AdmitOutcome.StoreFull, 0);
        if (!await WriteAsync(command).ConfigureAwait(false))
            return null;

        bool newest = content.Kind == EntryKind.Version && await RecordNewestVersionAsync(content).ConfigureAwait(false);
        await StoreRowAsync(command, ObjectLinks.Of(entry, newest)).ConfigureAwait(false);
        ulong storeSeq = ++state.StoreSeq;
        byte[] hashKey = RowKeys.Of(entry.Hash.Value);
        var row = new EntryRow
        {
            Hash = hashKey,
            StoreSeq = storeSeq,
            Package = RowKeys.Of(id.Value),
            Device = RowKeys.Of(content.Device.Value),
            Clock = content.Clock,
            Parents = [.. content.Parents.SelectMany(p => RowKeys.Of(p.Value))],
            Kind = content.Kind,
            Target = content.Payload is RedactionPayload named ? RowKeys.Of(named.Target.Value) : null,
            StoredMs = now,
            Redacted = await IsRedactedAsync(content.Facts, hashKey).ConfigureAwait(false),
        };
        await tables.Entries.PutAsync(row, CancellationToken.None).ConfigureAwait(false);
        if (content.Payload is RedactionPayload redaction)
            await RedactAsync(content, redaction.Target).ConfigureAwait(false);

        await UpdateHeadAsync(command, content).ConfigureAwait(false);
        await pins.PinAsync(entry.Hash, PinReason.Head, id.Value).ConfigureAwait(false);

        IReadOnlyList<ObjectHash> references = content.Payload.References;
        foreach (ObjectHash reference in references)
            await PutReferenceAsync(entry.Hash, reference).ConfigureAwait(false);

        await pins.ApplyPolicyAsync(package, references, content.Kind, newest).ConfigureAwait(false);
        if (state.Trust.Self is { } self && content.Device == self.Device)
        {
            foreach (ObjectHash reference in references)
                await pins.PinClosureAsync(reference, ObjectTag.Entry, PinReason.Outbound, id.Value, static (_, _) => true).ConfigureAwait(false);
        }

        Changes.Entries.Add(new StoredEntry(entry.Hash, storeSeq, id, content.Key, content.Kind, row.Redacted, now, command.Origin));
        return new AdmitResult(AdmitOutcome.Stored, storeSeq);
    }

    private async Task<AdmitResult?> AdmitUnsignedAsync(AdmitCommand command)
    {
        if (Overflows(command))
            return new AdmitResult(AdmitOutcome.StoreFull, 0);
        if (!await WriteAsync(command).ConfigureAwait(false))
            return null;

        await StoreRowAsync(command, ObjectLinks.Of(command.Object, false)).ConfigureAwait(false);
        IEnumerable<ObjectHash> children = command.Object switch
        {
            Tree tree => tree.Entries.Select(e => e.Hash),
            ChunkList list => list.Chunks,
            _ => [],
        };
        foreach (ObjectHash child in children.Distinct())
            await PutReferenceAsync(command.Object.Hash, child).ConfigureAwait(false);

        await pins.InheritAsync(command.Object.Hash, state.Trust).ConfigureAwait(false);
        return new AdmitResult(AdmitOutcome.Stored, 0);
    }

    // A local write must not raise the pinned bytes above the budget: eviction cannot free pinned bytes (LOCAL-SPEC.md §6.5).
    private bool Overflows(AdmitCommand command) =>
        command.Origin.Kind == OriginKind.Local && state.PinnedBytes + command.Object.Bytes.Length > options.BudgetBytes;

    private async Task<bool> WriteAsync(AdmitCommand command)
    {
        try
        {
            await files.WriteAtomicAsync(command.Object.Hash, command.Object.Bytes, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            command.Fail(new MeshStoreException(
                $"The object {command.Object.Hash} could not be written to the store: {e.Message}",
                e,
                "Check that the store directory exists, is writable by the node's user and has free space.",
                "Then repeat the request; nothing was stored."));
            return false;
        }
    }

    // SPEC.md §8.6: once its parents are held, an Entry naming a parent of another Package, a clock not above a parent's,
    // or a clock more than 2^32 above the greatest is faulty. An Entry stored before its parents (a history walk) is not
    // checked again when they arrive (STRUCTURAL-DEBT.md 28).
    private async Task<bool> HasParentFaultAsync(EntryContent content)
    {
        if (content.Parents.IsEmpty)
            return false;
        var parents = new EntryFacts[content.Parents.Count];
        for (int i = 0; i < parents.Length; i++)
        {
            EntryRow? parent = await tables.Entries.FindAsync(RowKeys.Of(content.Parents[i].Value), CancellationToken.None).ConfigureAwait(false);
            if (parent is null)
                return false;
            parents[i] = FactsOf(parent);
        }

        return Lamport.TryFindParentFault(content, parents, out _);
    }

    private bool Withdraw(WithdrawGrantCommand command)
    {
        if (!state.Trust.WithdrawOwnGrant(command.Package))
            return false;
        Changes.Package(new PackageChange(command.Package, PackageChangeKind.Grant, command.Origin));
        Changes.TrustChanged = true;
        return true;
    }

    private async Task StoreRowAsync(AdmitCommand command, ObjectLinks links)
    {
        StorableObject storable = command.Object;
        long size = storable.Bytes.Length;
        await tables.Objects.PutAsync(new ObjectRow
        {
            Hash = RowKeys.Of(storable.Hash.Value),
            Tag = (byte)storable.Tag,
            Size = size,
            StoredMs = now,
            LastUsedMs = now,
        }, CancellationToken.None).ConfigureAwait(false);
        state.Objects++;
        state.Bytes += size;
        Changes.Stored.Add(new StoredObject(storable.Hash, (byte)storable.Tag, size, command.Origin, command.Context) { Links = links });
    }

    private Task PutReferenceAsync(ObjectHash referrer, ObjectHash target) => tables.References.PutAsync(new ReferenceRow
    {
        Key = RowKeys.Of(referrer.Value, target.Value),
        Referrer = RowKeys.Of(referrer.Value),
        Target = RowKeys.Of(target.Value),
    }, CancellationToken.None);

    private async Task PutDeviceAsync(UserId user, DeviceId device, PublicKey key, string name, bool listed, DeviceRemoval removal, bool removedEver) =>
        await tables.Devices.PutAsync(new DeviceRow
        {
            Key = RowKeys.Of(user.Value, device.Value),
            User = RowKeys.Of(user.Value),
            Device = RowKeys.Of(device.Value),
            DeviceKey = key.Encoded.ToArray(),
            Name = name,
            Listed = listed,
            Removal = removal,
            RemovedEver = removedEver,
        }, CancellationToken.None).ConfigureAwait(false);

    private async Task<PackageRow> PackageRowAsync(PackageId id) =>
        await tables.Packages.FindAsync(RowKeys.Of(id.Value), CancellationToken.None).ConfigureAwait(false)
        ?? new PackageRow { Package = RowKeys.Of(id.Value) };

    // ---- Entries: Heads, redactions, the newest Version ----------------------------------------------------------------

    private async Task UpdateHeadAsync(AdmitCommand command, EntryContent content)
    {
        byte[] key = RowKeys.Of(content.Package.Value, content.Device.Value);
        HeadRow? current = await tables.Heads.FindAsync(key, CancellationToken.None).ConfigureAwait(false);
        var candidate = new Head(content.Device, content.Hash, content.Clock);
        if (current is not null && !HeadRule.Supersedes(candidate, new Head(content.Device, new ObjectHash(RowKeys.Hash(current.Entry)), current.Clock)))
            return;

        await tables.Heads.PutAsync(new HeadRow
        {
            Key = key,
            Package = RowKeys.Of(content.Package.Value),
            Device = RowKeys.Of(content.Device.Value),
            Entry = RowKeys.Of(content.Hash.Value),
            Clock = content.Clock,
        }, CancellationToken.None).ConfigureAwait(false);
        state.Heads = state.Heads.With(content.Package, candidate);
        Changes.Package(new PackageChange(content.Package, PackageChangeKind.Head, command.Origin));
    }

    // SPEC.md §9.4: a Redaction applies to its target whenever both are stored, whatever their order.
    private async Task<bool> IsRedactedAsync(EntryFacts target, byte[] targetKey)
    {
        List<EntryRow> naming = await tables.Entries.Query()
            .Where(EntryRow.Columns.Target == targetKey)
            .ToListAsync(CancellationToken.None).ConfigureAwait(false);
        return naming.Any(r => Redactions.Honours(FactsOf(r), target, state.Trust.IsOwnerDevice(target.Package, new DeviceId(RowKeys.Hash(r.Device)))));
    }

    private async Task RedactAsync(EntryContent redaction, ObjectHash target)
    {
        EntryRow? row = await tables.Entries.FindAsync(RowKeys.Of(target.Value), CancellationToken.None).ConfigureAwait(false);
        if (row is null || row.Redacted || !Redactions.Honours(redaction.Facts, FactsOf(row), state.Trust.IsOwnerDevice(redaction.Package, redaction.Device)))
            return;

        row.Redacted = true;
        await tables.Entries.PutAsync(row, CancellationToken.None).ConfigureAwait(false);
        Changes.Redacted.Add(target);
    }

    private async Task<bool> RecordNewestVersionAsync(EntryContent content)
    {
        PackageRow row = await PackageRowAsync(content.Package).ConfigureAwait(false);
        if (row.NewestVersion is { } newest && content.Key <= new EntryKey(row.NewestVersionClock, new DeviceId(RowKeys.Hash(row.NewestVersionDevice!)), new ObjectHash(RowKeys.Hash(newest))))
            return false;

        row.NewestVersion = RowKeys.Of(content.Hash.Value);
        row.NewestVersionClock = content.Clock;
        row.NewestVersionDevice = RowKeys.Of(content.Device.Value);
        await tables.Packages.PutAsync(row, CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private static EntryFacts FactsOf(EntryRow row) => new(new PackageId(RowKeys.Hash(row.Package)), row.Kind, EntryIndex.KeyOf(row));

    // ---- Pins, Packages, identity, peers ------------------------------------------------------------------------------

    private async Task<int> PinAsync(PinCommand command)
    {
        int changed = 0;
        switch (command.Kind)
        {
            case PinCommandKind.Pin:
                foreach (ObjectHash hash in command.Hashes)
                    changed += await pins.PinAsync(hash, command.Reason, command.Source).ConfigureAwait(false) ? 1 : 0;
                break;
            case PinCommandKind.Unpin:
                foreach (ObjectHash hash in command.Hashes)
                    changed += await pins.UnpinAsync(hash, command.Reason, command.Source).ConfigureAwait(false) ? 1 : 0;
                break;
            case PinCommandKind.Release:
                changed = await pins.ReleaseAsync(command.Reason, command.Source).ConfigureAwait(false);
                break;
            case PinCommandKind.Sent:
                foreach (ObjectHash hash in command.Hashes)
                    changed += await pins.ReleaseOutboundAsync(hash).ConfigureAwait(false);
                break;
        }

        return changed;
    }

    private async Task<bool> PackageAsync(PackageCommand command)
    {
        PackageId id = command.Package;
        state.Trust.TryGetPackage(id, out PackageTrust? package);
        PackageState current = package?.State ?? PackageState.None;
        (PackageState next, LocalRetention policy, PackageChangeKind change) = command.Kind switch
        {
            PackageCommandKind.List when current == PackageState.None => (PackageState.Listed, LocalRetention.Default, PackageChangeKind.Listed),
            PackageCommandKind.Follow when current != PackageState.Followed => (PackageState.Followed, package?.Policy ?? LocalRetention.Default, PackageChangeKind.Followed),
            PackageCommandKind.Unfollow when current == PackageState.Followed && !IsOwn(package!) => (PackageState.Listed, package!.Policy, PackageChangeKind.Unfollowed),
            PackageCommandKind.SetPolicy when package is not null && current != PackageState.None && package.Policy != command.Policy => (current, command.Policy, PackageChangeKind.Policy),
            _ => (current, LocalRetention.Default, PackageChangeKind.None),
        };
        if (change == PackageChangeKind.None)
            return false;

        state.Trust.SetState(id, next);
        package = state.Trust.SetPolicy(id, policy);
        PackageRow row = await PackageRowAsync(id).ConfigureAwait(false);
        row.State = next;
        row.Policy = policy;
        if (change == PackageChangeKind.Followed)
            row.FollowedSinceMs = now;
        await tables.Packages.PutAsync(row, CancellationToken.None).ConfigureAwait(false);

        if (change is PackageChangeKind.Unfollowed or PackageChangeKind.Policy)
            await pins.ReleaseAsync(PinReason.Policy, id.Value).ConfigureAwait(false);
        if (change == PackageChangeKind.Unfollowed)
            await pins.ReleaseAsync(PinReason.Head, id.Value).ConfigureAwait(false);
        if (change == PackageChangeKind.Followed)
            await PinHistoryAsync(package).ConfigureAwait(false);
        if (change is PackageChangeKind.Followed or PackageChangeKind.Policy && next == PackageState.Followed && package.Manifest is not null)
            await ApplyPolicyToStoredAsync(package, row).ConfigureAwait(false);

        Changes.Package(new PackageChange(id, change, Origin.Local));
        Changes.TrustChanged = true;
        return true;
    }

    private bool IsOwn(PackageTrust package) =>
        package.Manifest is { } manifest && state.Trust.Self is { } self && manifest.Content.Owner == self.User;

    // SPEC.md §11.2: the Manifest, Grants and every Entry of a followed Package carry reason Head.
    private async Task PinHistoryAsync(PackageTrust package)
    {
        Hash32 source = package.Id.Value;
        if (package.Manifest is { } manifest)
            await pins.PinAsync(manifest.Hash, PinReason.Head, source).ConfigureAwait(false);
        foreach (VerifiedGrant grant in package.Grants.Values)
            await pins.PinAsync(grant.Hash, PinReason.Head, source).ConfigureAwait(false);
        foreach (EntryRow entry in await EntriesOfAsync(package.Id).ConfigureAwait(false))
            await pins.PinAsync(new ObjectHash(RowKeys.Hash(entry.Hash)), PinReason.Head, source).ConfigureAwait(false);
    }

    private async Task ApplyPolicyToStoredAsync(PackageTrust package, PackageRow row)
    {
        foreach (EntryRow entry in await EntriesOfAsync(package.Id).ConfigureAwait(false))
        {
            var hash = new ObjectHash(RowKeys.Hash(entry.Hash));
            bool newest = row.NewestVersion is { } newestVersion && newestVersion.AsSpan().SequenceEqual(entry.Hash);
            await pins.ApplyPolicyAsync(package, await pins.ChildrenAsync(hash).ConfigureAwait(false), entry.Kind, newest).ConfigureAwait(false);
        }
    }

    private Task<List<EntryRow>> EntriesOfAsync(PackageId package) =>
        tables.Entries.Query().Where(EntryRow.Columns.Package == RowKeys.Of(package.Value)).ToListAsync(CancellationToken.None);

    private async Task<bool> IdentityAsync(SelfIdentity identity)
    {
        if (state.Trust.Self == identity)
            return false;

        state.Trust.SetSelf(identity);
        await tables.Meta.PutAsync(new MetaRow { Name = MetaKeys.SelfUserKey, Bytes = identity.UserKey.Encoded.ToArray() }, CancellationToken.None).ConfigureAwait(false);
        await tables.Meta.PutAsync(new MetaRow { Name = MetaKeys.SelfDevice, Bytes = RowKeys.Of(identity.Device.Value) }, CancellationToken.None).ConfigureAwait(false);
        await tables.Meta.PutAsync(new MetaRow { Name = MetaKeys.SelfName, Bytes = Encoding.UTF8.GetBytes(identity.UserName) }, CancellationToken.None).ConfigureAwait(false);
        await tables.Meta.PutAsync(new MetaRow { Name = MetaKeys.SelfCreated, Number = (ulong)Math.Max(0, identity.CreatedMs) }, CancellationToken.None).ConfigureAwait(false);
        await PutPairedAsync(identity.UserKey).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> PairAsync(PublicKey userKey)
    {
        if (!state.Trust.Pair(userKey))
            return false;
        await PutPairedAsync(userKey).ConfigureAwait(false);
        return true;
    }

    private async Task PutPairedAsync(PublicKey userKey)
    {
        UserId user = userKey.AsUserId();
        byte[] key = RowKeys.Of(user.Value);
        UserRow row = await tables.Users.FindAsync(key, CancellationToken.None).ConfigureAwait(false)
            ?? new UserRow { User = key, UserKey = userKey.Encoded.ToArray(), SinceMs = now };
        row.Paired = true;
        await tables.Users.PutAsync(row, CancellationToken.None).ConfigureAwait(false);
        Changes.Users.Add(user);
        Changes.TrustChanged = true;
    }

    private async Task PeerAsync(PeerCommand command)
    {
        byte[] key = RowKeys.Of(command.Device.Value);
        PeerRow row = await tables.Peers.FindAsync(key, CancellationToken.None).ConfigureAwait(false)
            ?? new PeerRow { Device = key, FirstSeenMs = now };
        row.LastSeenMs = now;
        if (command.User is { } user)
            row.User = RowKeys.Of(user.Value);
        if (command.Address is { Length: > 0 } address)
            row.Addresses = string.Join('\n', new[] { address }.Concat(row.Addresses.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(a => a != address)).Take(PeerAddresses));
        if (command.BackoffUntilMs is { } backoff)
            row.BackoffUntilMs = backoff;
        if (command.Violation)
        {
            bool windowOver = now - row.ViolationsSinceMs >= ViolationWindowMs;
            row.Violations = windowOver ? 1 : row.Violations + 1;
            row.ViolationsSinceMs = windowOver ? now : row.ViolationsSinceMs;
        }

        await tables.Peers.PutAsync(row, CancellationToken.None).ConfigureAwait(false);
    }

    // ---- Eviction ---------------------------------------------------------------------------------------------------

    private async Task<EvictionOutcome> EvictAsync(ObjectHash hash)
    {
        ObjectRow? row = await tables.Objects.FindAsync(RowKeys.Of(hash.Value), CancellationToken.None).ConfigureAwait(false);
        if (row is null)
            return EvictionOutcome.NotStored;
        if (row.PinCount > 0)
            return EvictionOutcome.Pinned;
        return promises.IsProtected(hash) ? EvictionOutcome.Promised : await UnlinkAsync(row, hash).ConfigureAwait(false);
    }

    private async Task<EvictionReport> EvictUnderBudgetAsync()
    {
        int evicted = 0;
        int later = 0;
        long bytes = 0;
        if (state.Bytes > options.BudgetBytes)
        {
            List<ObjectRow> candidates = await tables.Objects.Query()
                .Where(ObjectRow.Columns.PinCount == 0)
                .OrderBy(ObjectRow.Columns.LastUsedMs)
                .ToListAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (ObjectRow row in candidates)
            {
                if (state.Bytes <= options.BudgetBytes)
                    break;
                var hash = new ObjectHash(RowKeys.Hash(row.Hash));
                if (promises.IsProtected(hash))
                    continue;

                EvictionOutcome outcome = await UnlinkAsync(row, hash).ConfigureAwait(false);
                evicted += outcome == EvictionOutcome.Evicted ? 1 : 0;
                bytes += outcome == EvictionOutcome.Evicted ? row.Size : 0;
                later += outcome == EvictionOutcome.Later ? 1 : 0;
            }
        }

        return new EvictionReport(evicted, bytes, later, state.Bytes <= options.BudgetBytes);
    }

    // LOCAL-SPEC.md §9.7: a delete a reader blocks is "evict later"; the object counts as stored until the unlink succeeded.
    private async Task<EvictionOutcome> UnlinkAsync(ObjectRow row, ObjectHash hash)
    {
        if (files.TryDelete(hash) == ObjectDeletion.InUse)
        {
            if (!row.EvictPending)
            {
                row.EvictPending = true;
                await tables.Objects.PutAsync(row, CancellationToken.None).ConfigureAwait(false);
            }

            return EvictionOutcome.Later;
        }

        await tables.Objects.DeleteAsync(row.Hash, CancellationToken.None).ConfigureAwait(false);
        await tables.References.DeleteWhereAsync(ReferenceRow.Columns.Referrer == row.Hash, CancellationToken.None).ConfigureAwait(false);
        if ((ObjectTag)row.Tag == ObjectTag.Entry)
        {
            if (await tables.Entries.FindAsync(row.Hash, CancellationToken.None).ConfigureAwait(false) is { } entry)
                state.Heads = state.Heads.Without(new PackageId(RowKeys.Hash(entry.Package)), hash);
            await tables.Entries.DeleteAsync(row.Hash, CancellationToken.None).ConfigureAwait(false);
            await tables.Heads.DeleteWhereAsync(HeadRow.Columns.Entry == row.Hash, CancellationToken.None).ConfigureAwait(false);
        }

        state.Objects--;
        state.Bytes -= row.Size;
        Changes.Evicted.Add(hash);
        return EvictionOutcome.Evicted;
    }
}
