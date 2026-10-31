using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The node's side of replication that follows the store (SPEC.md §10.3, §10.6): one subscription to the
/// <see cref="ChangeBus"/>, and for every committed batch it records the objects stored, ends the wants they satisfy,
/// walks history from what peers sent (parents until held Entries, then payload objects as far as the retention policy
/// goes), re-examines the held buffer after a trust change, and cancels the wants of an unfollowed Package. It also keeps
/// the held buffer's clock: what a peer sent is dropped 10 minutes after it disconnected.
/// </summary>
internal sealed class Replicator : IDisposable
{
    private const int Backlog = 4096;
    private static readonly TimeSpan HeldWindow = TimeSpan.FromSeconds(Limits.HeldAfterDisconnectSeconds);

    private readonly StoreWriter writer;
    private readonly ChangeBus bus;
    private readonly Intake intake;
    private readonly WantScheduler wants;
    private readonly NodeFacts facts;
    private readonly TelemetrySink telemetry;
    private readonly TimeProvider time;
    private readonly ITimer expiry;
    private readonly TransferLog transfers;
    private ChangeSubscription? subscription;
    private int working;

    /// <summary>The replicator of a node.</summary>
    /// <param name="writer">The store writer.</param>
    /// <param name="bus">Its change bus.</param>
    /// <param name="intake">Intake, whose held buffer it re-examines and expires.</param>
    /// <param name="wants">The want scheduler.</param>
    /// <param name="facts">Where stored objects are recorded.</param>
    /// <param name="telemetry">The counters.</param>
    /// <param name="time">The node's clock.</param>
    /// <param name="transfers">Where each object a peer sent and the store took is reported.</param>
    public Replicator(StoreWriter writer, ChangeBus bus, Intake intake, WantScheduler wants, NodeFacts facts, TelemetrySink telemetry, TimeProvider time, TransferLog transfers)
    {
        this.transfers = transfers;
        this.writer = writer;
        this.bus = bus;
        this.intake = intake;
        this.wants = wants;
        this.facts = facts;
        this.telemetry = telemetry;
        this.time = time;
        subscription = bus.Subscribe(Backlog);
        expiry = time.CreateTimer(static state => ((Replicator)state!).Expire(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Whether no change set waits and none is being handled.</summary>
    public bool IsIdle => Volatile.Read(ref working) == 0 && (Volatile.Read(ref subscription)?.Reader.Count ?? 0) == 0;

    /// <summary>Follows the change bus until cancelled; a lapsed subscription is taken up again.</summary>
    /// <param name="cancellationToken">Stops it.</param>
    /// <returns>A task that completes when stopped.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ChangeSubscription current = Volatile.Read(ref subscription) ?? bus.Subscribe(Backlog);
            Volatile.Write(ref subscription, current);
            try
            {
                while (await current.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref working);
                    try
                    {
                        while (current.Reader.TryRead(out ChangeSet? set))
                            await HandleAsync(set, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref working);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Lapsed: the walks of the lost change sets are lost; the next listing plans them again (SPEC.md §10.1).
            current.Dispose();
            Volatile.Write(ref subscription, null);
        }
    }

    /// <summary>
    /// A peer's session ended: what it sent and is still held goes 10 minutes from now unless it comes back. One timer
    /// serves every peer; it fires 10 minutes after the latest end, and drops whatever is due by then.
    /// </summary>
    /// <param name="device">The peer.</param>
    public void SessionEnded(DeviceId device)
    {
        intake.OriginGone(Origin.FromPeer(device), time.GetUtcNow().ToUnixTimeMilliseconds());
        expiry.Change(HeldWindow, Timeout.InfiniteTimeSpan);
    }

    /// <summary>A peer's session was established: what it sent stays held.</summary>
    /// <param name="device">The peer.</param>
    public void SessionStarted(DeviceId device) => intake.Held.OriginBack(Origin.FromPeer(device));

    /// <inheritdoc />
    public void Dispose()
    {
        expiry.Dispose();
        Interlocked.Exchange(ref subscription, null)?.Dispose();
    }

    private void Expire() => intake.Expire(time.GetUtcNow().ToUnixTimeMilliseconds());

    private async Task HandleAsync(ChangeSet set, CancellationToken cancellationToken)
    {
        telemetry.Stored(set.Entries.Count);
        foreach (StoredObject stored in set.Stored)
        {
            facts.Record(NodeFactKind.ObjectStored, 0, null, stored.Origin.Peer, stored.Hash.Value, $"{(ObjectTag)stored.Tag}, {stored.Size} bytes, from {stored.Origin}");
            if (stored.Origin.Kind == OriginKind.Peer && stored.Origin.Peer is { } peer)
                transfers.Record(peer, stored.Context ?? default, stored.Hash, TransferDirection.In, stored.Size, TransferState.Stored);
            wants.Settle(stored.Hash);
        }

        foreach (ObjectHash evicted in set.Evicted)
            facts.Record(NodeFactKind.ObjectEvicted, 0, null, default, evicted.Value, "evicted");

        await WalkAsync(set, cancellationToken).ConfigureAwait(false);

        foreach (PackageChange change in set.Packages)
        {
            if (change.Kind == PackageChangeKind.Unfollowed)
                wants.CancelPackage(change.Package);
        }

        if (set.TrustChanged)
            await intake.ReexamineAsync(cancellationToken).ConfigureAwait(false);
    }

    // SPEC.md §10.3: from every object a peer sent, its parents and the payload objects the policy keeps, unless held.
    private async Task WalkAsync(ChangeSet set, CancellationToken cancellationToken)
    {
        Dictionary<ObjectHash, PackageId>? entries = null;
        LdbContext? context = null;
        try
        {
            TrustSnapshot trust = writer.Trust;
            foreach (StoredObject stored in set.Stored)
            {
                if (stored.Origin.Kind != OriginKind.Peer || (stored.Links.Parents.Count == 0 && stored.Links.Children.Count == 0))
                    continue;

                PackageId? package = stored.Context;
                if ((ObjectTag)stored.Tag == ObjectTag.Entry)
                {
                    entries ??= EntriesOf(set);
                    package = entries.TryGetValue(stored.Hash, out PackageId of) ? of : null;
                }

                if (package is not { } id || !trust.TryGetPackage(id, out PackageTrust? held) || !held.IsFollowed || held.Manifest is not { } manifest)
                    continue;

                RetentionRow row = RetentionRule.Effective(writer.Options.DeviceClass, held.Policy, manifest.Content.Retention);
                IReadOnlyList<PlannedWant> walk = ReplicationPlanner.Walk((ObjectTag)stored.Tag, stored.Links, row, manifest.Content.Kind);
                if (walk.Count == 0)
                    continue;

                context ??= await LdbContext.OpenAsync(writer.Database, cancellationToken).ConfigureAwait(false);
                var missing = new List<PlannedWant>(walk.Count);
                foreach (PlannedWant want in walk)
                {
                    if (!wants.IsWanted(want.Hash) && !intake.Held.Contains(want.Hash)
                        && await context.Set<ObjectRow>().FindAsync(RowKeys.Of(want.Hash.Value), cancellationToken).ConfigureAwait(false) is null)
                    {
                        missing.Add(want);
                    }
                }

                wants.Want(missing, id, stored.Origin.Peer);
            }
        }
        finally
        {
            if (context is not null)
                await context.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Dictionary<ObjectHash, PackageId> EntriesOf(ChangeSet set)
    {
        var entries = new Dictionary<ObjectHash, PackageId>(set.Entries.Count);
        foreach (StoredEntry entry in set.Entries)
            entries[entry.Hash] = entry.Package;
        return entries;
    }
}
