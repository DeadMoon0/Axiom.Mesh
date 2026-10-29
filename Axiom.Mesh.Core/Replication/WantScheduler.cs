using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core;

/// <summary>
/// Every object this node wants, and from whom (SPEC.md §10.3–§10.4, §7.5): one per node, fed by the sessions (listings,
/// <c>Have</c>, answers) and the history walk. A want is addressed to a source that claimed the hash with <c>Have</c>,
/// listed it, or sent the object that names it, spread by each peer's outstanding count; at most 64 hashes are
/// outstanding toward one peer; one that does not answer within 30 seconds is asked of another source as well; a
/// <c>NotFound</c> removes that source; an object that arrives settles its want, and the asks still outstanding elsewhere
/// are cancelled. Implements <see cref="IWantedSet"/> for intake.
/// </summary>
/// <remarks>
/// One lock guards the state; messages are collected under it and handed to the sessions' outboxes after it is left. A
/// want without any source waits until a <c>Have</c> names it. Thread-safe.
/// </remarks>
internal sealed class WantScheduler : IWantedSet, IDisposable
{
    private static readonly TimeSpan Reask = TimeSpan.FromSeconds(Limits.ReaskSeconds);
    private static readonly TimeSpan MinimumWait = TimeSpan.FromMilliseconds(1);

    private readonly object gate = new();
    private readonly TimeProvider time;
    private readonly Dictionary<ObjectHash, Wanted> wants = [];
    private readonly Dictionary<DeviceId, Peer> peers = [];
    private readonly List<Peer> attached = [];
    private readonly ITimer timer;
    private bool disposed;

    /// <summary>A scheduler on the node's clock.</summary>
    /// <param name="time">The clock the 30-second re-ask runs on.</param>
    public WantScheduler(TimeProvider time)
    {
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        timer = time.CreateTimer(static state => ((WantScheduler)state!).Pump(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Hashes asked of some peer and not yet answered.</summary>
    public int Outstanding
    {
        get
        {
            lock (gate)
                return wants.Values.Count(w => w.Asks.Count > 0);
        }
    }

    /// <summary>Hashes wanted that no attached peer is known to hold: they wait for a <c>Have</c>.</summary>
    public int Waiting
    {
        get
        {
            lock (gate)
                return wants.Values.Count(w => !w.Arrived && w.Asks.Count == 0 && !w.Sources.Any(peers.ContainsKey));
        }
    }

    /// <summary>
    /// Whether nothing is asked and nothing can be asked now: every want left waits for a source.
    /// </summary>
    public bool IsIdle
    {
        get
        {
            lock (gate)
                return wants.Values.All(w => w.Arrived || (w.Asks.Count == 0 && !w.Sources.Any(peers.ContainsKey)));
        }
    }

    /// <summary>How many hashes this node has outstanding toward a peer (LOCAL-SPEC.md §5.8 <c>wantsOut</c>).</summary>
    /// <param name="device">The peer.</param>
    /// <returns>The count.</returns>
    public int OutstandingAt(DeviceId device)
    {
        lock (gate)
            return peers.TryGetValue(device, out Peer? peer) ? peer.Budget.Outstanding : 0;
    }

    /// <inheritdoc />
    public bool IsWanted(ObjectHash hash)
    {
        lock (gate)
            return wants.ContainsKey(hash);
    }

    /// <summary>Why a hash is wanted, if it is.</summary>
    /// <param name="hash">The hash.</param>
    /// <param name="reason">The reason, or <c>default</c>.</param>
    /// <returns><see langword="true"/> when wanted.</returns>
    public bool TryGetReason(ObjectHash hash, out WantReason reason)
    {
        lock (gate)
        {
            reason = wants.TryGetValue(hash, out Wanted? want) ? want.Reason : default;
            return want is not null;
        }
    }

    /// <summary>
    /// A session to a peer is established: wants may be addressed to it through <paramref name="send"/>. While two sessions
    /// to one device are attached (the duplicate rule is closing one, SPEC.md §4.5), the one attached last is used.
    /// </summary>
    /// <param name="device">The peer.</param>
    /// <param name="connection">The session's connection.</param>
    /// <param name="send">Puts a <c>Want</c> or <c>Cancel</c> on the session's control lane; never blocks.</param>
    public void Attach(DeviceId device, long connection, Action<MeshMessage> send)
    {
        ArgumentNullException.ThrowIfNull(send);
        lock (gate)
        {
            var peer = new Peer(device, connection, send);
            attached.Add(peer);
            peers[device] = peer;
        }

        Pump();
    }

    /// <summary>A session to a peer ended: what was asked on it is asked elsewhere, and, if it was the current one, the peer's claims are forgotten.</summary>
    /// <param name="device">The peer.</param>
    /// <param name="connection">The session's connection.</param>
    public void Detach(DeviceId device, long connection)
    {
        lock (gate)
        {
            attached.RemoveAll(p => p.Device == device && p.Connection == connection);
            if (attached.LastOrDefault(p => p.Device == device) is { } other)
                peers[device] = other;
            else
                peers.Remove(device);
            foreach (Wanted want in wants.Values)
                want.Asks.RemoveAll(a => a.Peer.Device == device && a.Peer.Connection == connection);
        }

        Pump();
    }

    /// <summary>Wants objects; a hash already wanted gains the source.</summary>
    /// <param name="planned">The hashes and why.</param>
    /// <param name="package">The Package they are wanted for.</param>
    /// <param name="source">A peer that holds them (listed them, or sent what names them); or <see langword="null"/>.</param>
    public void Want(IReadOnlyList<PlannedWant> planned, PackageId package, DeviceId? source)
    {
        ArgumentNullException.ThrowIfNull(planned);
        if (planned.Count == 0)
            return;
        lock (gate)
        {
            foreach (PlannedWant one in planned)
            {
                if (!wants.TryGetValue(one.Hash, out Wanted? want))
                {
                    wants[one.Hash] = want = new Wanted(new WantReason(package, one.Kind));
                    foreach ((DeviceId device, Peer peer) in peers)
                    {
                        if (peer.Haves.Contains(one.Hash))
                            want.AddSource(device);
                    }
                }

                if (source is { } from)
                    want.AddSource(from);
            }
        }

        Pump();
    }

    /// <summary>A peer claims objects (SPEC.md §7.4): recorded as a source, for wants now and later.</summary>
    /// <param name="device">The peer.</param>
    /// <param name="hashes">What it claims.</param>
    public void Have(DeviceId device, IReadOnlyList<ObjectHash> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        bool gained = false;
        lock (gate)
        {
            if (!peers.TryGetValue(device, out Peer? peer))
                return;
            foreach (ObjectHash hash in hashes)
            {
                peer.Haves.Add(hash);
                if (wants.TryGetValue(hash, out Wanted? want))
                    gained |= want.AddSource(device);
            }
        }

        if (gained)
            Pump();
    }

    /// <summary>
    /// An object arrived from a peer: an ask of it there is settled, and it is asked of nobody any more. The want itself
    /// stays until <see cref="Settle"/>, so intake still finds it wanted and a plan made before it is stored does not want
    /// it again.
    /// </summary>
    /// <param name="device">The peer that sent it.</param>
    /// <param name="hash">The object.</param>
    /// <param name="reason">Why it was wanted, or <c>default</c> when it was not.</param>
    /// <returns><see langword="true"/> when it was wanted.</returns>
    public bool Arrived(DeviceId device, ObjectHash hash, out WantReason reason)
    {
        lock (gate)
        {
            reason = default;
            if (!wants.TryGetValue(hash, out Wanted? want))
                return false;
            reason = want.Reason;
            want.Arrived = true;
            SettleAsks(want, device);
            return true;
        }
    }

    /// <summary>
    /// An object that was wanted is dealt with: stored, held, dropped or faulty. The want ends; asks still outstanding at
    /// other peers are cancelled (SPEC.md §7.8).
    /// </summary>
    /// <param name="hash">The object.</param>
    public void Settle(ObjectHash hash)
    {
        List<(Peer Peer, ObjectHash Hash)> cancels = [];
        lock (gate)
        {
            if (!wants.Remove(hash, out Wanted? want))
                return;
            foreach (Ask ask in want.Asks)
            {
                ask.Peer.Budget = ask.Peer.Budget.Settle(1);
                cancels.Add((ask.Peer, hash));
            }
        }

        foreach ((Peer peer, ObjectHash cancelled) in cancels)
            peer.Send(new Cancel([cancelled]));
        Pump();
    }

    /// <summary>A peer answered <c>NotFound</c>: it is no source for the hash until it claims it again (SPEC.md §10.4).</summary>
    /// <param name="device">The peer.</param>
    /// <param name="hash">The object.</param>
    public void NotFound(DeviceId device, ObjectHash hash)
    {
        lock (gate)
        {
            if (peers.TryGetValue(device, out Peer? peer))
                peer.Haves.Remove(hash);
            if (!wants.TryGetValue(hash, out Wanted? want))
                return;
            want.Sources.Remove(device);
            SettleAsks(want, device);
        }

        Pump();
    }

    /// <summary>Stops wanting everything wanted for a Package: it was unfollowed. Outstanding asks are cancelled.</summary>
    /// <param name="package">The Package.</param>
    public void CancelPackage(PackageId package)
    {
        var cancels = new Dictionary<Peer, List<ObjectHash>>();
        lock (gate)
        {
            foreach ((ObjectHash hash, Wanted want) in wants.Where(w => w.Value.Reason.Package == package).ToList())
            {
                wants.Remove(hash);
                foreach (Ask ask in want.Asks)
                {
                    ask.Peer.Budget = ask.Peer.Budget.Settle(1);
                    if (!cancels.TryGetValue(ask.Peer, out List<ObjectHash>? list))
                        cancels[ask.Peer] = list = [];
                    list.Add(hash);
                }
            }
        }

        foreach ((Peer peer, List<ObjectHash> hashes) in cancels)
        {
            foreach (ObjectHash[] chunk in hashes.Chunk(Limits.MaxCancelHashes))
                peer.Send(new Cancel(chunk));
        }

        Pump();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
            disposed = true;
        timer.Dispose();
    }

    // Addresses every want that can be asked now: not yet asked, or asked once more than 30 seconds ago with another source
    // left. Each goes to the source with the fewest outstanding, within the 64 of each peer (SPEC.md §7.5, §10.3, §10.4).
    private void Pump()
    {
        var batches = new Dictionary<Peer, List<ObjectHash>>();
        lock (gate)
        {
            if (disposed)
                return;
            DateTimeOffset now = time.GetUtcNow();
            DateTimeOffset? nextDue = null;
            foreach ((ObjectHash hash, Wanted want) in wants)
            {
                if (want.Arrived)
                    continue;
                bool due = want.Asks.Count == 0 || (want.Asks.Count == 1 && now - want.Asks[0].At >= Reask);
                if (!due)
                {
                    if (want.Asks.Count == 1)
                        nextDue = Earliest(nextDue, want.Asks[0].At + Reask);
                    continue;
                }

                Peer? best = null;
                foreach (DeviceId source in want.Sources)
                {
                    if (!peers.TryGetValue(source, out Peer? peer) || peer.Budget.Remaining == 0 || want.Asks.Exists(a => a.Peer.Device == source))
                        continue;
                    if (best is null || peer.Budget.Outstanding < best.Budget.Outstanding)
                        best = peer;
                }

                if (best is null)
                    continue;
                best.Budget.TryWant(1, out WantBudget next);
                best.Budget = next;
                want.Asks.Add(new Ask(best, now));
                nextDue = Earliest(nextDue, now + Reask);
                if (!batches.TryGetValue(best, out List<ObjectHash>? batch))
                    batches[best] = batch = [];
                batch.Add(hash);
            }

            if (nextDue is { } due2)
            {
                // Never zero: a fake clock would fire it inside this lock, in the middle of this pass.
                TimeSpan wait = due2 - now;
                timer.Change(wait < MinimumWait ? MinimumWait : wait, Timeout.InfiniteTimeSpan);
            }
            else
            {
                timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        foreach ((Peer peer, List<ObjectHash> hashes) in batches)
        {
            foreach (ObjectHash[] chunk in hashes.Chunk(Limits.MaxWantHashes))
                peer.Send(new Want(chunk));
        }
    }

    private static DateTimeOffset Earliest(DateTimeOffset? current, DateTimeOffset candidate) =>
        current is { } known && known <= candidate ? known : candidate;

    // The asks of a want made of one device, on whichever of its sessions, are answered.
    private static void SettleAsks(Wanted want, DeviceId device)
    {
        for (int i = want.Asks.Count - 1; i >= 0; i--)
        {
            Ask ask = want.Asks[i];
            if (ask.Peer.Device != device)
                continue;
            ask.Peer.Budget = ask.Peer.Budget.Settle(1);
            want.Asks.RemoveAt(i);
        }
    }

    private readonly record struct Ask(Peer Peer, DateTimeOffset At);

    private sealed class Wanted(WantReason reason)
    {
        public WantReason Reason { get; } = reason;

        public List<DeviceId> Sources { get; } = [];

        public List<Ask> Asks { get; } = [];

        public bool Arrived { get; set; }

        public bool AddSource(DeviceId device)
        {
            if (Sources.Contains(device))
                return false;
            Sources.Add(device);
            return true;
        }
    }

    private sealed class Peer(DeviceId device, long connection, Action<MeshMessage> send)
    {
        public DeviceId Device { get; } = device;

        public long Connection { get; } = connection;

        public Action<MeshMessage> Send { get; } = send;

        public WantBudget Budget { get; set; } = WantBudget.Empty;

        public SourceBook Haves { get; } = new();
    }
}
