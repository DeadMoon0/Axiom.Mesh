using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TransferDirection = Axiom.Mesh.Local.TransferDirection;
using TransferState = Axiom.Mesh.Local.TransferState;
using Axiom.LDB.Entity;
using Axiom.Mesh.Protocol;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Errors;
using WireState = Axiom.Mesh.Protocol.PackageState;

namespace Axiom.Mesh.Core;

/// <summary>
/// Replication over one established session (SPEC.md §7.2–§7.9, §10): a thin actor of three loops. The read loop
/// handles what the peer sends, in order: listings through <see cref="ListingRule"/> into the planner, <c>Have</c> and
/// answers into the <see cref="WantScheduler"/>, objects into <see cref="Intake"/>, <c>Want</c>s answered after a
/// visibility check, <c>RosterQuery</c> under §9.8. The write loop drains the <see cref="SessionOutbox"/>, control lane
/// first, reading each object from the store as it goes out. The push loop follows the node's one change subscription
/// of this session: the listing on establishment and whenever the set the peer may see changes, <c>PackageChanged</c>
/// and <c>Have</c> for what changed, never back to the peer a change came from, and the own Roster when its seq rises.
/// </summary>
internal sealed class ReplicationSession : IDisposable
{
    private const int ChangeBacklog = 1024;
    private const int SentBatch = 64;

    private readonly NodeReplication node;
    private readonly PeerSession tracked;
    private readonly Session<MeshMessage, MeshMessage> session;
    private readonly SessionOutbox outbox = new();
    private readonly ServedObjects served;
    private readonly CancellationTokenSource violated = new();
    private readonly object theirsGate = new();
    private readonly HashSet<PackageId> listedHere = [];
    private readonly HashSet<ObjectHash> revocationsSent = [];
    private readonly List<ObjectHash> sentOutbound = new(SentBatch);
    private readonly ITimer listingTimer;
    private readonly DateTimeOffset since;
    private Dictionary<PackageId, WireState> theirs = [];
    private Dictionary<PackageId, WireState> partial = [];
    private HashSet<PackageId> listed = [];
    private ListingRule listing = ListingRule.Idle;
    private ChangeSubscription? changes;
    private string? violation;
    private ulong rosterSeq;
    private long handled;
    private long objectsIn;
    private long objectsOut;
    private int reading;
    private int pushing;
    private int started;

    /// <summary>Replication over a session whose <c>Hello</c>s were exchanged and checked.</summary>
    /// <param name="node">What the node shares.</param>
    /// <param name="tracked">The session as the peer table tracks it.</param>
    /// <param name="session">The AMP/1 session, in Established.</param>
    /// <param name="rosterSeq">The seq of the own Roster this node's <c>Hello</c> named, or the Roster it sent after it.</param>
    /// <param name="sentBefore">Messages sent after the <c>Hello</c> before this loop starts (the Roster follow-up).</param>
    public ReplicationSession(NodeReplication node, PeerSession tracked, Session<MeshMessage, MeshMessage> session, ulong rosterSeq, int sentBefore)
    {
        this.node = node;
        this.tracked = tracked;
        this.session = session;
        this.rosterSeq = rosterSeq;
        SentBefore = sentBefore;
        served = new ServedObjects(tracked.Peer.User);
        since = node.Time.GetUtcNow();
        listingTimer = node.Time.CreateTimer(static state => ((ReplicationSession)state!).CheckListing(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The connection's number.</summary>
    public long Connection => tracked.Connection;

    /// <summary>The peer device.</summary>
    public DeviceId Peer => tracked.Device;

    /// <summary>Whether the peer's current listing names a Package (LOCAL-SPEC.md §5.4 <c>listedBy</c>).</summary>
    /// <param name="package">The Package.</param>
    /// <returns><see langword="true"/> when it lists it.</returns>
    public bool Lists(PackageId package)
    {
        lock (theirsGate)
            return theirs.ContainsKey(package);
    }

    private int SentBefore { get; }

    private DeviceId Device => tracked.Device;

    private UserId User => tracked.Peer.User;

    private Origin From => Origin.FromPeer(tracked.Device);

    /// <summary>What the session carried and holds queued.</summary>
    /// <returns>A snapshot.</returns>
    public SessionTraffic Traffic() => new()
    {
        Device = Device,
        User = User,
        Connection = Connection,
        Since = since,
        BytesIn = tracked.Activity.BytesIn,
        BytesOut = tracked.Activity.BytesOut,
        ObjectsIn = Interlocked.Read(ref objectsIn),
        ObjectsOut = Interlocked.Read(ref objectsOut),
        WantsOut = node.Wants.OutstandingAt(Device),
        WantsIn = outbox.WantsIn,
        QueuedObjectBytes = outbox.QueuedObjectBytes,
        DeferredWants = outbox.Deferred,
        MessagesSent = SentBefore + outbox.MessagesSent,
        MessagesHandled = Interlocked.Read(ref handled),
        IsIdle = Volatile.Read(ref started) == 1 && Volatile.Read(ref reading) == 0 && Volatile.Read(ref pushing) == 0
            && (Volatile.Read(ref changes)?.Reader.Count ?? 0) == 0 && outbox.IsIdle,
    };

    /// <summary>
    /// The peer broke a rule this side checks outside the definition (SPEC.md §5.1 frame completion, §7.2 listing time):
    /// the read loop ends and the session closes with <c>Bye(Violation)</c>. The first one counts.
    /// </summary>
    /// <param name="detail">What the peer did.</param>
    public void RaiseViolation(string detail)
    {
        if (Interlocked.CompareExchange(ref violation, detail, null) is not null)
            return;
        try
        {
            violated.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The session ended meanwhile.
        }
    }

    /// <summary>Runs the session until the peer leaves, a rule is broken, or <paramref name="stopping"/> is cancelled.</summary>
    /// <param name="stopping">Cancelled when the node drains or the session is asked to close.</param>
    /// <returns>How it ended.</returns>
    public async Task<SessionEnd> RunAsync(CancellationToken stopping)
    {
        Volatile.Write(ref changes, node.Bus.Subscribe(ChangeBacklog));
        node.Opened(this);
        node.Wants.Attach(Device, Connection, outbox.Send);
        node.Replicator.SessionStarted(Device);
        using var loops = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _ = Task.Run(() => WriteLoopAsync(loops.Token), CancellationToken.None);
        Task pushingLoop = Task.Run(() => PushLoopAsync(loops.Token), CancellationToken.None);
        try
        {
            return await ReadLoopAsync(stopping).ConfigureAwait(false);
        }
        finally
        {
            node.Wants.Detach(Device, Connection);
            foreach (ObjectHash unsent in outbox.Complete())
                node.Store.Promises.Release(Device, unsent);
            // The write loop is not awaited: a write the peer does not take blocks until the connection is disposed, which
            // happens only once this returns. It ends then, and gives back what it sent first.
            await loops.CancelAsync().ConfigureAwait(false);
            await pushingLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            node.Store.Promises.ReleaseAll(Device);
            node.Replicator.SessionEnded(Device);
            node.Closed(this);
            Interlocked.Exchange(ref changes, null)?.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        listingTimer.Dispose();
        violated.Dispose();
        outbox.Dispose();
    }

    // ---- Read loop ------------------------------------------------------------------------------------------------------

    private async Task<SessionEnd> ReadLoopAsync(CancellationToken stopping)
    {
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(stopping, violated.Token);
        while (true)
        {
            try
            {
                ProtocolReadResult<MeshMessage> read = await session.ReadAsync(reading.Token).ConfigureAwait(false);
                if (read.IsCompleted)
                    return new SessionEnd(session.State == Amp.Closed ? SessionEndKind.PeerBye : SessionEndKind.PeerClosed, string.Empty);
                if (read.Message is Bye)
                    return new SessionEnd(SessionEndKind.PeerBye, string.Empty);

                Interlocked.Increment(ref this.reading);
                try
                {
                    if (await HandleAsync(read.Message, reading.Token).ConfigureAwait(false) is { } broken)
                        return SessionEnd.Violation(broken);
                }
                finally
                {
                    Interlocked.Increment(ref handled);
                    Interlocked.Decrement(ref this.reading);
                }
            }
            catch (OperationCanceledException) when (violated.IsCancellationRequested)
            {
                return SessionEnd.Violation(Volatile.Read(ref violation) ?? "a rule of SPEC.md was broken");
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return new SessionEnd(SessionEndKind.Cancelled, string.Empty);
            }
        }
    }

    // One message; a broken rule is returned as its description, never thrown.
    private async ValueTask<string?> HandleAsync(MeshMessage? message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case Packages page:
                return await OnPackagesAsync(page, cancellationToken).ConfigureAwait(false);
            case PackageChanged changed:
                return await OnPackageChangedAsync(changed.State, cancellationToken).ConfigureAwait(false);
            case Have have:
                node.Wants.Have(Device, have.Hashes);
                return null;
            case Want want:
                if (!outbox.TryAcceptWant(want.Hashes.Count))
                    return $"a Want of {want.Hashes.Count} hashes past {Limits.MaxOutstandingWants} outstanding (SPEC.md §7.5)";
                await AnswerAsync(want.Hashes, cancellationToken).ConfigureAwait(false);
                return null;
            case ObjectMessage received:
                return await OnObjectAsync(received, cancellationToken).ConfigureAwait(false);
            case NotFound notFound:
                node.Wants.NotFound(Device, notFound.Hash);
                return null;
            case Cancel cancel:
                foreach (ObjectHash dropped in outbox.Cancel(cancel.Hashes))
                    node.Store.Promises.Release(Device, dropped);
                return null;
            case RosterQuery query:
                ServeRoster(query.User);
                return null;
            default:
                return null; // RosterNotFound: nothing to do; the definition consumes Ping, Pong, extensions and ServiceOpen.
        }
    }

    private async ValueTask<string?> OnPackagesAsync(Packages page, CancellationToken cancellationToken)
    {
        ListingStep step = listing.OnPackages(page, node.Time.GetUtcNow());
        if (step.Verdict == ListingVerdict.Violation)
            return "a listing that breaks SPEC.md §7.2: " + step.Detail;
        listing = step.Next;
        ArmListing();
        if (page.Page == 0)
            partial = [];
        foreach (WireState state in page.States)
            partial[state.Package] = state;
        if (step.Verdict != ListingVerdict.Completed)
            return null;

        Dictionary<PackageId, WireState> previous;
        lock (theirsGate)
        {
            previous = theirs;
            theirs = partial;
        }

        partial = [];
        node.Facts.Record(NodeFactKind.ListingDone, Connection, tracked.Remote, Device, $"{theirs.Count} packages");
        foreach (PackageId omitted in previous.Keys)
        {
            if (!theirs.ContainsKey(omitted))
                await OwnerOmitsAsync(omitted, cancellationToken).ConfigureAwait(false);
        }

        foreach (WireState state in theirs.Values)
            await ConsiderAsync(state, cancellationToken).ConfigureAwait(false);
        return null;
    }

    private async ValueTask<string?> OnPackageChangedAsync(WireState state, CancellationToken cancellationToken)
    {
        ListingStep step = listing.OnPackageChanged(node.Time.GetUtcNow());
        if (step.Verdict == ListingVerdict.Violation)
            return "a PackageChanged that breaks SPEC.md §7.2: " + step.Detail;
        lock (theirsGate)
            theirs[state.Package] = state;
        await ConsiderAsync(state, cancellationToken).ConfigureAwait(false);
        return null;
    }

    // SPEC.md §10.2 for one listed Package; one this node does not know becomes Listed first (§1.2, §10.6).
    private async ValueTask ConsiderAsync(WireState state, CancellationToken cancellationToken)
    {
        TrustSnapshot trust = node.Writer.Trust;
        if (!trust.TryGetPackage(state.Package, out _))
        {
            if (listedHere.Add(state.Package))
                await PostAsync(new PackageCommand(PackageCommandKind.List, state.Package, LocalRetention.Default), cancellationToken).ConfigureAwait(false);
            return;
        }

        node.Wants.Want(ReplicationPlanner.Plan(PackageView.Of(state.Package, trust, node.Writer.Heads), state, trust), state.Package, Device);
    }

    // SPEC.md §7.2: a device of the owner whose listing omits a Package it listed before withdraws this node's own Grant.
    private async ValueTask OwnerOmitsAsync(PackageId package, CancellationToken cancellationToken)
    {
        TrustSnapshot trust = node.Writer.Trust;
        if (trust.Self is { } self && trust.TryGetPackage(package, out PackageTrust? held) && held.Manifest is { } manifest
            && manifest.Content.Owner == User && self.User != User && held.Grants.ContainsKey(self.User))
        {
            await PostAsync(new WithdrawGrantCommand(package, From), cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask AnswerAsync(IReadOnlyList<ObjectHash> hashes, CancellationToken cancellationToken)
    {
        TrustSnapshot trust = node.Writer.Trust;
        await using LdbContext context = await LdbContext.OpenAsync(node.Writer.Database, cancellationToken).ConfigureAwait(false);
        foreach (ObjectHash hash in hashes)
        {
            if (await served.ResolveAsync(context, hash, trust, cancellationToken).ConfigureAwait(false) is { } size)
            {
                // SPEC.md §11.3: what a peer wants is not evicted before it went out.
                node.Store.Promises.Protect(Device, hash);
                outbox.Answer(hash, size);
            }
            else
            {
                outbox.NotFound(hash);
                node.Facts.Record(NodeFactKind.WantAnswered, Connection, null, Device, hash.Value, "NotFound");
                node.Transfers.Record(Device, default, hash, TransferDirection.Out, 0, TransferState.NotFound);
            }
        }
    }

    private async ValueTask<string?> OnObjectAsync(ObjectMessage received, CancellationToken cancellationToken)
    {
        ObjectHash hash = received.Hash;
        ReadOnlyMemory<byte> bytes = received.Bytes;
        bool entry = !bytes.IsEmpty && bytes.Span[0] == (byte)ObjectTag.Entry;
        bool wanted = node.Wants.Arrived(Device, hash, out WantReason reason);
        Interlocked.Increment(ref objectsIn);
        node.Telemetry.ObjectIn();
        long nowMs = node.Time.GetUtcNow().ToUnixTimeMilliseconds();
        // An object received whole is taken in even when the session closes meanwhile: it was verified, and dropping it
        // would leave its want answered and unstored.
        IntakeOutcome outcome;
        try
        {
            outcome = await node.Intake.ReceiveAsync(hash, bytes, From, wanted && !reason.Package.Value.IsZero ? reason.Package : null, nowMs, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            if (wanted)
                node.Wants.Settle(hash);
            throw;
        }

        switch (outcome.Verdict)
        {
            case IntakeVerdict.Violation:
                return outcome.Detail;
            case IntakeVerdict.Faulty:
                node.Table.Faulty(Device, Connection, hash, outcome.Detail ?? "faulty content");
                node.Transfers.Record(Device, reason.Package, hash, TransferDirection.In, bytes.Length, TransferState.Dropped);
                break;
            case IntakeVerdict.Admitted when outcome.Stored is { } stored:
                // The want ends once the store decided, so no plan made meanwhile wants the object again.
                _ = ObserveStoredAsync(stored, hash, entry, wanted);
                return null;
        }

        if (wanted)
            node.Wants.Settle(hash);
        return null;
    }

    // SPEC.md §8.6: an Entry whose held parents fail the clock rules is faulty content, refused by the writer.
    private async Task ObserveStoredAsync(Task<AdmitResult> stored, ObjectHash hash, bool entry, bool wanted)
    {
        try
        {
            if ((await stored.ConfigureAwait(false)).Outcome == AdmitOutcome.Faulty && entry)
                node.Table.Faulty(Device, Connection, hash, "an Entry whose parents fail SPEC.md §8.6 (another Package, a clock not above a parent's, or more than 2^32 above)");
        }
        catch (Exception ex) when (ex is MeshStoreException or ObjectDisposedException or IOException)
        {
            // The store failed or stopped; the writer reports that itself.
        }
        finally
        {
            if (wanted)
                node.Wants.Settle(hash);
        }
    }

    // SPEC.md §7.9, §8.8, §9.8: a Roster of a known user that the peer may see, else RosterNotFound.
    private void ServeRoster(UserId user)
    {
        TrustSnapshot trust = node.Writer.Trust;
        if (trust.IsKnownUser(user) && trust.MayServeRoster(user, User) && trust.TryGetUser(user, out UserTrust? held) && held.Roster is { } roster)
            outbox.Send(ObjectMessage.From(roster));
        else
            outbox.Send(new RosterNotFound(user));
    }

    private async ValueTask PostAsync<T>(StoreCommand<T> command, CancellationToken cancellationToken)
    {
        try
        {
            _ = await node.Writer.EnqueueAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The node stops; nothing more is stored.
        }
    }

    private void ArmListing()
    {
        if (listing.Deadline is { } deadline)
        {
            TimeSpan left = deadline - node.Time.GetUtcNow();
            listingTimer.Change(left > TimeSpan.Zero ? left + TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
        }
        else
        {
            listingTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void CheckListing()
    {
        if (listing.IsOverdue(node.Time.GetUtcNow()))
            RaiseViolation($"a listing that did not complete within {Limits.ListingSeconds} seconds of its page 0 (SPEC.md §7.2)");
    }

    // ---- Write loop -----------------------------------------------------------------------------------------------------

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await outbox.TakeAsync(cancellationToken).ConfigureAwait(false) is { } item)
            {
                try
                {
                    if (item.Message is { } message)
                        await session.WriteAsync(message, cancellationToken).ConfigureAwait(false);
                    else
                        await SendObjectAsync(item.Hash, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    outbox.Done(item);
                }

                if (sentOutbound.Count >= SentBatch || (sentOutbound.Count > 0 && outbox.IsIdle))
                    await FlushSentAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ProtoStreamException or IOException or ObjectDisposedException)
        {
            // The session ends: the read loop sees it too.
        }
        finally
        {
            await FlushSentAsync().ConfigureAwait(false);
        }
    }

    // An object goes out as the store holds it (SPEC.md §11.1): read into one pooled buffer and written as it is.
    private async Task SendObjectAsync(ObjectHash hash, CancellationToken cancellationToken)
    {
        try
        {
            if (!node.Store.TryOpen(hash, out ObjectHandle? handle))
            {
                await session.WriteAsync(new NotFound(hash), cancellationToken).ConfigureAwait(false);
                node.Facts.Record(NodeFactKind.WantAnswered, Connection, null, Device, hash.Value, "NotFound: gone from the store");
                node.Transfers.Record(Device, default, hash, TransferDirection.Out, 0, TransferState.NotFound);
                return;
            }

            int length = (int)Math.Min(handle.Length, Limits.MaxObjectLength);
            byte[] buffer = node.SendPool.Rent(Math.Max(1, length));
            try
            {
                using (handle)
                    await handle.Stream.ReadExactlyAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                await session.WriteAsync(ObjectMessage.FromStored(hash, buffer.AsMemory(0, length)), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                node.SendPool.Return(buffer);
            }

            Interlocked.Increment(ref objectsOut);
            node.Telemetry.ObjectOut();
            sentOutbound.Add(hash);
            node.Facts.Record(NodeFactKind.WantAnswered, Connection, null, Device, hash.Value, $"Object, {length} bytes");
            node.Transfers.Record(Device, default, hash, TransferDirection.Out, length, TransferState.Sent);
        }
        finally
        {
            node.Store.Promises.Release(Device, hash);
        }
    }

    // SPEC.md §11.2: an object sent once in answer to a Want loses its Outbound pin.
    private async Task FlushSentAsync()
    {
        if (sentOutbound.Count == 0)
            return;
        ObjectHash[] sent = [.. sentOutbound];
        sentOutbound.Clear();
        try
        {
            _ = await node.Writer.EnqueueAsync(new PinCommand(PinCommandKind.Sent, sent, PinReason.Outbound, default), CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The node stops; the pins stay and the objects are sent again after the next start.
        }
    }

    // ---- Push loop ------------------------------------------------------------------------------------------------------

    private async Task PushLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            TrustSnapshot first = node.Writer.Trust;
            SendListing(first);
            OfferOwnRoster(first);
            Volatile.Write(ref started, 1);
            while (!cancellationToken.IsCancellationRequested)
            {
                ChangeSubscription current = Volatile.Read(ref changes) ?? throw new ObjectDisposedException(nameof(ReplicationSession));
                while (await current.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref pushing);
                    try
                    {
                        while (current.Reader.TryRead(out ChangeSet? set))
                            Push(set);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref pushing);
                    }
                }

                // Lapsed: what changed meanwhile is unknown, so the whole listing goes again (SPEC.md §10.1).
                if (!current.Lapsed)
                    return;
                ChangeSubscription renewed = node.Bus.Subscribe(ChangeBacklog);
                if (Interlocked.CompareExchange(ref changes, renewed, current) != current)
                {
                    renewed.Dispose();
                    return;
                }

                current.Dispose();
                listed = [];
                TrustSnapshot again = node.Writer.Trust;
                SendListing(again);
                OfferOwnRoster(again);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The session ends.
        }
    }

    private void SendListing(TrustSnapshot trust)
    {
        listed = Listings.Listable(User, trust);
        outbox.SendAll(Listings.Pages(User, listed, trust, node.Writer.Heads));
    }

    // SPEC.md §7.2, §7.3, §7.6, §9.7, §10.5 for one committed batch.
    private void Push(ChangeSet set)
    {
        TrustSnapshot trust = node.Writer.Trust;
        HeadsView heads = node.Writer.Heads;
        HashSet<PackageId> listable = set.TrustChanged ? Listings.Listable(User, trust) : listed;
        var messages = new List<MeshMessage>();
        var changed = new HashSet<PackageId>();
        foreach (PackageChange change in set.Packages)
        {
            if (change.Kind is PackageChangeKind.Followed or PackageChangeKind.Manifest or PackageChangeKind.Grant or PackageChangeKind.Listed)
                Replan(change.Package, trust, heads);
            if (change.Origin == From || change.Kind is not (PackageChangeKind.Manifest or PackageChangeKind.Grant or PackageChangeKind.Head))
                continue;
            if (!trust.TryGetPackage(change.Package, out PackageTrust? package) || package.Manifest is null)
                continue;

            if (listable.Contains(change.Package) && listed.Contains(change.Package))
            {
                if (changed.Add(change.Package))
                    messages.Add(new PackageChanged(Listings.StateFor(package, User, heads)));
            }
            else if (change.Kind == PackageChangeKind.Grant && !listable.Contains(change.Package)
                && package.Grants.TryGetValue(User, out VerifiedGrant? revocation) && revocationsSent.Add(revocation.Hash))
            {
                // SPEC.md §9.7: the Grant naming the peer's user reaches it whatever its rights, so a revocation arrives.
                messages.Add(new PackageChanged(Listings.RevocationFor(package, revocation)));
            }
        }

        if (!listable.SetEquals(listed))
        {
            listed = listable;
            messages.AddRange(Listings.Pages(User, listed, trust, heads));
        }

        if (messages.Count > 0)
            outbox.SendAll(messages);
        ClaimStored(set, trust);
        PushOwnRoster(set, trust);
    }

    // SPEC.md §7.4, §10.5: Have for the objects stored of Packages the peer may see, never for what came from it.
    private void ClaimStored(ChangeSet set, TrustSnapshot trust)
    {
        if (set.Stored.Count == 0)
            return;
        Dictionary<ObjectHash, PackageId>? entries = null;
        List<ObjectHash>? claims = null;
        foreach (StoredObject stored in set.Stored)
        {
            if (stored.Origin == From)
                continue;
            PackageId? package = stored.Context;
            switch ((ObjectTag)stored.Tag)
            {
                case ObjectTag.Roster or ObjectTag.Manifest or ObjectTag.Grant:
                    continue; // listed and pushed as state; a Roster only as §7.6 says
                case ObjectTag.Entry:
                    entries ??= EntriesOf(set);
                    package = entries.TryGetValue(stored.Hash, out PackageId of) ? of : null;
                    break;
            }

            if (package is { } id && trust.MayList(User, id))
                (claims ??= []).Add(stored.Hash);
        }

        if (claims is not null)
            outbox.TrySendHave(claims);
    }

    // SPEC.md §7.6: the own user's new Roster goes, unsolicited, to every established peer when its seq rises.
    private void PushOwnRoster(ChangeSet set, TrustSnapshot trust)
    {
        if (trust.Self is { } self && set.Users.Contains(self.User))
            OfferOwnRoster(trust);
    }

    // The own Roster when it is newer than the one this session's Hello named or last sent. Checked once the loop has
    // subscribed to the changes too: a Roster stored after the Hello went out but before the subscription began came in no
    // change set this session sees, and would otherwise never reach the peer.
    private void OfferOwnRoster(TrustSnapshot trust)
    {
        if (trust.Self is { } self && trust.TryGetUser(self.User, out UserTrust? own) && own.Roster is { } roster && roster.Content.Seq > rosterSeq)
        {
            rosterSeq = roster.Content.Seq;
            outbox.Send(ObjectMessage.From(roster));
        }
    }

    private void Replan(PackageId package, TrustSnapshot trust, HeadsView heads)
    {
        WireState? state;
        lock (theirsGate)
            theirs.TryGetValue(package, out state);
        if (state is not null)
            node.Wants.Want(ReplicationPlanner.Plan(PackageView.Of(package, trust, heads), state, trust), package, Device);
    }

    private static Dictionary<ObjectHash, PackageId> EntriesOf(ChangeSet set)
    {
        var entries = new Dictionary<ObjectHash, PackageId>(set.Entries.Count);
        foreach (StoredEntry entry in set.Entries)
            entries[entry.Hash] = entry.Package;
        return entries;
    }
}
