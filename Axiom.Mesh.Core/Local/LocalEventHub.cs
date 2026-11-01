using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Local;
using LocalPackageChange = Axiom.Mesh.Local.PackageChange;

namespace Axiom.Mesh.Core;

/// <summary>
/// Turns what the node does into the events of LOCAL-SPEC.md §7.3 and hands each to the subscriptions it matches, on
/// every connection, without blocking anything (§7.4): change sets of the store writer become <c>EntryStored</c>,
/// <c>PackageChanged</c>, <c>UserChanged</c> and <c>StoreChanged</c>; views of the peer table become <c>PeerChanged</c>.
/// Each event is made once and shared by every subscription that receives it; nothing is made for a topic nobody
/// subscribed to.
/// </summary>
/// <remarks>
/// Topic transfers carries what replication reports (<see cref="TransferChanged"/>), topic service what
/// <see cref="ServiceEmitted"/> is handed, topic pairing what the pairing shell reports (<see cref="PairingChanged"/>);
/// operations are the connection's own (<see cref="OperationTable"/>).
/// </remarks>
internal sealed class LocalEventHub
{
    private const int ChangeQueue = 1024;
    private static readonly TimeSpan StoreInterval = TimeSpan.FromSeconds(1);

    private readonly LocalPort port;
    private readonly ChangeBus bus;
    private readonly Channel<IReadOnlyList<PeerRecord>> views = Channel.CreateBounded<IReadOnlyList<PeerRecord>>(new BoundedChannelOptions(ChangeQueue)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private readonly object storeGate = new();
    private ChangeSubscription subscription;
    private TrustSnapshot? previous;
    private long lastStoreEmit = long.MinValue;
    private ITimer? storeTimer;

    /// <summary>The hub of a port.</summary>
    /// <param name="port">The port.</param>
    /// <param name="bus">The node's change bus; the hub subscribes at once so that nothing committed after the node started is missed.</param>
    public LocalEventHub(LocalPort port, ChangeBus bus)
    {
        this.port = port;
        this.bus = bus;
        subscription = bus.Subscribe(ChangeQueue);
    }

    /// <summary>
    /// Held while the hub offers a batch's Entries, and by a <c>Subscribe</c> to topic entries while it reads the newest store
    /// sequence, plans its replay and adds its subscription (LOCAL-SPEC.md §7.2: no gap and no repeat between the replay and
    /// the live events).
    /// </summary>
    public SemaphoreSlim EntriesGate { get; } = new(1, 1);

    /// <summary>A new view of the peer table; called under the table's lock, so it only queues.</summary>
    /// <param name="view">The view.</param>
    public void PeersChanged(IReadOnlyList<PeerRecord> view) => views.Writer.TryWrite(view);

    /// <summary>
    /// One object's step in or out (§5.16), as a <c>TransferChanged</c> on every subscription of topic transfers whose
    /// filter names its Package and peer, or neither (§7.2). Called from replication; it only queues.
    /// </summary>
    /// <param name="transfer">The step.</param>
    public void TransferChanged(Transfer transfer)
    {
        LocalConnection[] live = Subscribed(Topic.Transfers);
        if (live.Length == 0)
            return;
        var changed = new TransferChanged { Transfer = transfer };
        var queued = new QueuedEvent(changed, FrameSizes.OfEvent(changed), 0);
        foreach (LocalConnection connection in live)
        {
            connection.Offer(
                Topic.Transfers,
                f => (f.PackageId.Value.IsZero || f.PackageId == transfer.PackageId) && (f.DeviceId.Value.IsZero || f.DeviceId == transfer.Peer),
                queued);
        }
    }

    /// <summary>
    /// A service's event (§9.10) on every subscription of topic service whose filter names the service and the Package, or
    /// neither (§7.2). <c>Emit</c>, the request that calls it, is deferred (plan D7); the entry point is the Host's.
    /// </summary>
    /// <param name="emitted">The event.</param>
    public void ServiceEmitted(ServiceEvent emitted)
    {
        ArgumentNullException.ThrowIfNull(emitted);
        LocalConnection[] live = Subscribed(Topic.Service);
        if (live.Length == 0)
            return;
        var queued = new QueuedEvent(emitted, FrameSizes.OfEvent(emitted), 0);
        foreach (LocalConnection connection in live)
        {
            connection.Offer(
                Topic.Service,
                f => (f.Service.Length == 0 || string.Equals(f.Service, emitted.Service, StringComparison.Ordinal)) && (f.PackageId.Value.IsZero || f.PackageId == emitted.PackageId),
                queued);
        }
    }

    /// <summary>
    /// A pairing changed (§6.2): a <c>PairingChanged</c> on every subscription of topic pairing (§7.2). Called by the
    /// pairing shell after every transition; it only queues.
    /// </summary>
    /// <param name="pairing">The pairing after the change.</param>
    public void PairingChanged(Pairing pairing)
    {
        ArgumentNullException.ThrowIfNull(pairing);
        LocalConnection[] live = Subscribed(Topic.Pairing);
        if (live.Length == 0)
            return;
        var changed = new PairingChanged { Pairing = pairing };
        var queued = new QueuedEvent(changed, FrameSizes.OfEvent(changed), 0);
        foreach (LocalConnection connection in live)
            connection.Offer(Topic.Pairing, static _ => true, queued);
    }

    /// <summary>Turns change sets into events until stopped.</summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when stopped.</returns>
    public async Task RunChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await foreach (ChangeSet changes in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    await PublishAsync(changes, cancellationToken).ConfigureAwait(false);

                // The writer outran the hub: every live subscription that could have missed an event lapses (§7.4).
                if (!subscription.Lapsed)
                    return;
                subscription = bus.Subscribe(ChangeQueue);
                LapseEverything();
            }
        }
        finally
        {
            subscription.Dispose();
            lock (storeGate)
            {
                storeTimer?.Dispose();
                storeTimer = null;
            }
        }
    }

    /// <summary>Turns peer views into <c>PeerChanged</c> events until stopped.</summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when stopped.</returns>
    public async Task RunPeersAsync(CancellationToken cancellationToken)
    {
        var known = new Dictionary<DeviceId, PeerRecord>();
        await foreach (IReadOnlyList<PeerRecord> view in views.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var now = view.ToDictionary(p => p.Device);
            LocalConnection[] live = Subscribed(Topic.Peers);
            if (live.Length != 0)
            {
                foreach (PeerRecord peer in view)
                {
                    if (ChangeOf(known.GetValueOrDefault(peer.Device), peer) is { } change)
                        Publish(live, peer, change);
                }

                foreach (PeerRecord gone in known.Values.Where(p => !now.ContainsKey(p.Device)))
                    Publish(live, gone, PeerChange.Forgotten);
            }

            known = now;
        }
    }

    /// <summary>Reads the next Entry of a replay (§7.4): lazily, as the subscription's queue drains.</summary>
    /// <param name="storeSeq">Its store sequence.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The event; <see langword="null"/> when the Entry was evicted meanwhile.</returns>
    public async Task<QueuedEvent?> ReplayAsync(ulong storeSeq, CancellationToken cancellationToken)
    {
        StoreWriter writer = port.Node.Writer;
        return await EntryRecords.EventAtAsync(writer.Database, port.Node.Files, storeSeq, cancellationToken).ConfigureAwait(false);
    }

    private static PeerChange? ChangeOf(PeerRecord? before, PeerRecord after)
    {
        if (before is null)
            return after.State == PeerState.Established ? PeerChange.Established : PeerChange.Discovered;
        if (before.State != after.State)
        {
            return after.State switch
            {
                PeerState.Dialling => PeerChange.Dialling,
                PeerState.Established => PeerChange.Established,
                PeerState.Refused => PeerChange.RefusedByPeer,
                _ when before.State == PeerState.Established => PeerChange.Closed,
                _ when after.Violations > before.Violations => PeerChange.RefusedHere,
                _ => null,
            };
        }

        return after.Violations > before.Violations ? PeerChange.RefusedHere : null;
    }

    private void Publish(LocalConnection[] live, PeerRecord peer, PeerChange change)
    {
        MeshNode node = port.Node;
        Session? session = null;
        if (change is PeerChange.Established or PeerChange.Closed)
        {
            SessionTraffic? traffic = node.Replication.Sessions.FirstOrDefault(s => s.Device == peer.Device);
            session = traffic is not null
                ? LocalRecords.SessionOf(traffic, node.Table.EstablishedWith(peer.Device), port.Time.GetUtcNow())
                : new Session { DeviceId = peer.Device, UserId = peer.User, State = SessionState.Established };
        }

        var changed = new PeerChanged { Change = change, Peer = LocalRecords.PeerOf(peer, node.Trust), Session = session };
        var queued = new QueuedEvent(changed, FrameSizes.OfEvent(changed), 0);
        foreach (LocalConnection connection in live)
            connection.Offer(Topic.Peers, f => f.DeviceId.Value.IsZero || f.DeviceId == peer.Device, queued);
    }

    private async Task PublishAsync(ChangeSet changes, CancellationToken cancellationToken)
    {
        TrustSnapshot before = previous ?? changes.Trust;
        previous = changes.Trust;
        await PublishEntriesAsync(changes, cancellationToken).ConfigureAwait(false);
        await PublishPackagesAsync(changes, before, cancellationToken).ConfigureAwait(false);
        await PublishUsersAsync(changes, before, cancellationToken).ConfigureAwait(false);
        PublishStore();
    }

    private async Task PublishEntriesAsync(ChangeSet changes, CancellationToken cancellationToken)
    {
        if (changes.Entries.Count == 0)
            return;

        // A Subscribe to topic entries reads the newest store sequence and adds its subscription under the same gate, so an
        // Entry is either at or below what it read (its replay) or offered after it was added (live): never neither.
        await EntriesGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await OfferEntriesAsync(changes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            EntriesGate.Release();
        }
    }

    private async Task OfferEntriesAsync(ChangeSet changes, CancellationToken cancellationToken)
    {
        LocalConnection[] live = Subscribed(Topic.Entries);
        if (live.Length == 0)
            return;

        IObjectFiles files = port.Node.Files;
        foreach (StoredEntry stored in changes.Entries)
        {
            bool Match(Filter f) => FilterRule.Matches(f, stored.Package, (ushort)stored.Kind);
            if (!live.Any(c => c.Outbox.Of(Topic.Entries).Any(s => Match(s.Filter) && stored.StoreSeq > s.LiveAfter)))
                continue;
            if (await EntryRecords.ReadAsync(files, stored.Hash, stored.StoreSeq, stored.StoredMs, stored.Redacted, cancellationToken).ConfigureAwait(false) is not { } entry)
                continue;
            var event_ = new EntryStored { Entry = entry };
            var queued = new QueuedEvent(event_, FrameSizes.OfEvent(event_), stored.StoreSeq);
            foreach (LocalConnection connection in live)
                connection.Offer(Topic.Entries, Match, queued);
        }
    }

    private async Task PublishPackagesAsync(ChangeSet changes, TrustSnapshot before, CancellationToken cancellationToken)
    {
        if (changes.Packages.Count == 0)
            return;
        LocalConnection[] live = Subscribed(Topic.Packages);
        if (live.Length == 0)
            return;

        foreach (PackageChange change in changes.Packages)
        {
            // A dropped Package is no longer held: its last record comes from the trust before the batch, with no Heads
            // and no Entries left (§14.12 Dropped).
            Package record = change.Kind == PackageChangeKind.Dropped
                ? PackageRecords.Of(change.Package, before, [], 0, 0)
                : await PackageRecords.BuildAsync(port.Node, change.Package, cancellationToken).ConfigureAwait(false);
            bool Match(Filter f) => FilterRule.Matches(f, change.Package, (ushort)record.Kind);
            foreach (PackageChanged changed in EventsOf(change, record, changes, before))
            {
                var queued = new QueuedEvent(changed, FrameSizes.OfEvent(changed), 0);
                foreach (LocalConnection connection in live)
                    connection.Offer(Topic.Packages, Match, queued);
            }
        }
    }

    // A Grant change names the Grants that are new in this batch; a Head change the Heads of the devices whose Entries it
    // stored; every other change is one event (§7.3, §14.12).
    private IEnumerable<PackageChanged> EventsOf(PackageChange change, Package record, ChangeSet changes, TrustSnapshot before)
    {
        var kind = (LocalPackageChange)(byte)change.Kind;
        switch (change.Kind)
        {
            case PackageChangeKind.Grant:
                IReadOnlyDictionary<UserId, VerifiedGrant> was = before.TryGetPackage(change.Package, out PackageTrust? old) ? old.Grants : new Dictionary<UserId, VerifiedGrant>();
                if (changes.Trust.TryGetPackage(change.Package, out PackageTrust? now))
                {
                    foreach (VerifiedGrant grant in now.Grants.Values.Where(g => !was.TryGetValue(g.Content.Grantee, out VerifiedGrant? prior) || prior.Hash != g.Hash))
                        yield return new PackageChanged { Change = kind, Package = record, Grant = LocalRecords.GrantOf(grant) };
                }

                break;
            case PackageChangeKind.Head:
                HeadsView heads = port.Node.Writer.Heads;
                foreach (DeviceId device in changes.Entries.Where(e => e.Package == change.Package).Select(e => e.Key.Device).Distinct())
                {
                    if (heads.TryGet(change.Package, device, out Head head))
                        yield return new PackageChanged { Change = kind, Package = record, Head = head };
                }

                break;
            default:
                yield return new PackageChanged { Change = kind, Package = record };
                break;
        }
    }

    private async Task PublishUsersAsync(ChangeSet changes, TrustSnapshot before, CancellationToken cancellationToken)
    {
        if (changes.Users.Count == 0)
            return;
        LocalConnection[] live = Subscribed(Topic.Users);
        if (live.Length == 0)
            return;

        TrustSnapshot now = changes.Trust;
        await using LdbContext context = await LdbContext.OpenAsync(port.Node.Writer.Database, cancellationToken).ConfigureAwait(false);
        foreach (UserId user in changes.Users.Distinct())
        {
            bool knownBefore = before.IsKnownUser(user) && before.TryGetUser(user, out _);
            bool knownNow = now.TryGetUser(user, out UserTrust? trust) && now.IsKnownUser(user);
            UserChanged? changed;
            if (knownNow && trust is not null)
            {
                ulong seqBefore = before.TryGetUser(user, out UserTrust? old) ? old.Roster?.Content.Seq ?? 0 : 0;
                UserChange change = !knownBefore ? UserChange.Known : (trust.Roster?.Content.Seq ?? 0) != seqBefore ? UserChange.Roster : UserChange.Unassigned;
                if (change == UserChange.Unassigned)
                    continue;
                UserRow? row = await context.Set<UserRow>().FindAsync(RowKeys.Of(user.Value), cancellationToken).ConfigureAwait(false);
                string name = now.Self is { } self && self.User == user ? self.UserName : row?.Name ?? string.Empty;
                changed = new UserChanged
                {
                    Change = change,
                    User = LocalRecords.UserOf(trust, now.Self?.User, row?.SinceMs ?? 0, name),
                    Devices = LocalRecords.DevicesOf(trust, now.Self?.Device, port.Node.Table.View),
                };
            }
            else if (knownBefore)
            {
                changed = new UserChanged { Change = UserChange.Forgotten, User = new User { UserId = user } };
            }
            else
            {
                continue;
            }

            var queued = new QueuedEvent(changed, FrameSizes.OfEvent(changed), 0);
            foreach (LocalConnection connection in live)
                connection.Offer(Topic.Users, f => f.UserId.Value.IsZero || f.UserId == user, queued);
        }
    }

    // At most once per second (§7.2): the first change is sent at once, later ones within the second once it ends.
    private void PublishStore()
    {
        lock (storeGate)
        {
            long now = port.Time.GetTimestamp();
            TimeSpan since = lastStoreEmit == long.MinValue ? StoreInterval : port.Time.GetElapsedTime(lastStoreEmit, now);
            if (since >= StoreInterval)
            {
                lastStoreEmit = now;
                EmitStore();
                return;
            }

            storeTimer ??= port.Time.CreateTimer(static state => ((LocalEventHub)state!).StoreDue(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            storeTimer.Change(StoreInterval - since, Timeout.InfiniteTimeSpan);
        }
    }

    private void StoreDue()
    {
        lock (storeGate)
        {
            lastStoreEmit = port.Time.GetTimestamp();
            EmitStore();
        }
    }

    private void EmitStore()
    {
        LocalConnection[] live = Subscribed(Topic.Store);
        if (live.Length == 0)
            return;
        MeshNode node = port.Node;
        var changed = new StoreChanged { Store = LocalRecords.StoreOf(node.Writer.Totals, node.Files.StorePath, node.Replication.HeldBytes) };
        var queued = new QueuedEvent(changed, FrameSizes.OfEvent(changed), 0);
        foreach (LocalConnection connection in live)
            connection.Offer(Topic.Store, static _ => true, queued);
    }

    private void LapseEverything()
    {
        foreach (LocalConnection connection in port.Book.Live())
        {
            foreach (Topic topic in new[] { Topic.Entries, Topic.Packages, Topic.Users, Topic.Store })
            {
                foreach (LocalSubscription lapsing in connection.Outbox.Of(topic))
                    connection.Outbox.LapseNow(lapsing);
            }
        }
    }

    private LocalConnection[] Subscribed(Topic topic) =>
        [.. port.Book.Live().Where(c => c.Outbox.Of(topic).Length != 0)];
}
