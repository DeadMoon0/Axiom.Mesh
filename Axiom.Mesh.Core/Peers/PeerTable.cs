using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using Axiom.Mesh.Discovery;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// Every peer device the node knows of, and every static peer address, with where each stands (SPEC.md §4.4–§4.5,
/// §6.2, §12.1): fed by discovery (<see cref="Offer"/>), by the dialler (<see cref="DialEnded"/>) and by sessions
/// (<see cref="SessionOpened"/>, <see cref="Establish"/>, <see cref="RefusedBeforeHello"/>, <see cref="Violation"/>,
/// <see cref="SessionEnded"/>). It decides with the pure rules (<see cref="DialPolicy"/>, <see cref="Backoff"/>,
/// <see cref="DuplicateSessions"/>) and queues the dials it decides on for the dialler.
/// </summary>
/// <remarks>
/// One lock guards the table; nothing inside it waits or does I/O. Readers take <see cref="View"/>, an immutable
/// snapshot swapped after every change. A device that discovery announces but no known user's Roster lists gets no
/// entry: it costs the discovery cache's entry and nothing else.
/// </remarks>
internal sealed class PeerTable : IDisposable
{
    private const int MaxAddresses = 4;

    private readonly object gate = new();
    private readonly Dictionary<DeviceId, Entry> devices = [];
    private readonly Dictionary<IPEndPoint, StaticEntry> statics = [];
    private readonly Dictionary<IPEndPoint, DialRequest> attempts = [];
    private readonly Channel<DialRequest> requests = Channel.CreateUnbounded<DialRequest>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Func<TrustSnapshot> trust;
    private readonly TimeProvider time;
    private readonly NodeFacts facts;
    private PeerRecord[] view = [];
    private Action<IReadOnlyList<PeerRecord>>? changed;
    private bool stopped;

    /// <summary>An empty table.</summary>
    /// <param name="trust">The node's trust now.</param>
    /// <param name="time">The node's clock: backoffs and retries run on it.</param>
    /// <param name="facts">Where to record what happens.</param>
    public PeerTable(Func<TrustSnapshot> trust, TimeProvider time, NodeFacts facts)
    {
        this.trust = trust;
        this.time = time;
        this.facts = facts;
    }

    /// <summary>Every peer device, as of the last change.</summary>
    public IReadOnlyList<PeerRecord> View => Volatile.Read(ref view);

    /// <summary>The dials decided on, for the dialler; each address is dialled at most once at a time.</summary>
    public ChannelReader<DialRequest> Requests => requests.Reader;

    /// <summary>
    /// Hands every new view to <paramref name="observer"/>, in the order the views were made, under the table's lock: the
    /// local port turns them into <c>PeerChanged</c> events (LOCAL-SPEC.md §7.2). The observer must not block.
    /// </summary>
    /// <param name="observer">What to call; replaces the previous one.</param>
    public void OnChanged(Action<IReadOnlyList<PeerRecord>> observer)
    {
        lock (gate)
            changed = observer;
    }

    /// <summary>Takes an announcement: records the device if a known user's Roster lists it, and dials when the policy says so.</summary>
    /// <param name="announcement">What discovery heard, or a static peer's address.</param>
    public void Offer(Announcement announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);
        DialRequest? request;
        lock (gate)
        {
            if (stopped)
                return;
            DateTimeOffset now = time.GetUtcNow();
            request = announcement.Source.HasFlag(DiscoverySource.Static) ? OfferStatic(announcement.Address, now) : OfferDevice(announcement, now);
            Publish(now);
        }

        if (request is not null)
            requests.Writer.TryWrite(request);
    }

    /// <summary>Records how a dial ended. A dial that failed, or whose connection ended before the peer's <c>Hello</c>, backs off.</summary>
    /// <param name="request">The dial.</param>
    /// <param name="outcome">How it ended.</param>
    /// <param name="detail">Why, for the facts.</param>
    public void DialEnded(DialRequest request, DialOutcome outcome, string detail)
    {
        Pending? deviceFact = null;
        Pending? addressFact = null;
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            if (attempts.TryGetValue(request.Address, out DialRequest? running) && ReferenceEquals(running, request))
                attempts.Remove(request.Address);
            Entry? entry = request.Device is { } device ? devices.GetValueOrDefault(device) : null;
            if (entry is not null)
                entry.Dialling = false;
            StaticEntry? address = request.Device is null ? statics.GetValueOrDefault(request.Address) : null;
            if (address is not null)
            {
                address.Dialling = false;
                address.Device = request.Reached ?? address.Device;
                address.LastResult = request.Established ? DialResult.Established
                    : outcome == DialOutcome.Failed ? DialResult.Unreachable
                    : DialResult.Refused;
            }

            bool failed = outcome == DialOutcome.Failed || (outcome == DialOutcome.Ended && !request.Established);
            if (failed && !stopped)
            {
                // A connection that was served but never established was refused by the peer (SPEC.md §6.2).
                bool refused = outcome == DialOutcome.Ended;
                if (entry is not null)
                    deviceFact = Fail(entry, refused, detail, now);
                if (address is not null)
                    addressFact = Fail(address, detail, now);
            }
            else if (outcome == DialOutcome.Ended && address is not null)
            {
                address.Failures = 0;
                address.BackoffUntil = null;
            }

            Publish(now);
        }

        Record(deviceFact);
        Record(addressFact);
    }

    /// <summary>A TLS session to a peer whose key passed has opened; its <c>Hello</c> is awaited.</summary>
    /// <param name="session">The session.</param>
    public void SessionOpened(PeerSession session)
    {
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            Entry entry = EntryOf(session.Device);
            entry.User = session.Peer.User;
            entry.FirstSeen ??= now;
            entry.LastSeen = now;
            entry.Sessions.Add(session);
            if (session.IsDialledHere)
            {
                entry.AddAddress(session.Remote);
                if (attempts.TryGetValue(session.Remote, out DialRequest? attempt))
                    attempt.Reached = session.Device;
            }

            Publish(now);
        }
    }

    /// <summary>
    /// The peer's <c>Hello</c> passed: the session is established, unless the duplicate rule (SPEC.md §4.5) keeps
    /// another one to the same device. Resets the device's failures.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>Whether to keep it, and which older session to close.</returns>
    public EstablishVerdict Establish(PeerSession session)
    {
        EstablishVerdict verdict;
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            Entry entry = EntryOf(session.Device);
            if (session.IsDialledHere && attempts.TryGetValue(session.Remote, out DialRequest? attempt))
            {
                attempt.Established = true;
                if (attempt.Device is null && statics.TryGetValue(session.Remote, out StaticEntry? reached))
                    reached.LastResult = DialResult.Established;
            }
            entry.Failures = 0;
            entry.FailedUntil = null;
            entry.Refused = false;
            entry.FirstSeen ??= now;
            entry.LastSeen = now;

            PeerSession? existing = entry.Established;
            if (existing is null)
                verdict = new EstablishVerdict(true, null);
            else if (DuplicateSessions.Decide(existing.Activity.SilentFor(), existing.DialledBy, session.DialledBy) == KeptSession.New)
                verdict = new EstablishVerdict(true, existing);
            else
                verdict = new EstablishVerdict(false, null);

            if (verdict.KeepThis)
                entry.Established = session;
            Publish(now);
        }

        if (verdict.KeepThis)
            facts.Record(NodeFactKind.Established, session.Connection, session.Remote, session.Device, session.IsDialledHere ? "dialled here" : "dialled by the peer");
        return verdict;
    }

    /// <summary>
    /// The connection closed before the peer's <c>Hello</c>: a refusal of this node (SPEC.md §6.2). A connection this
    /// node dialled is counted when its dial ends; one the peer dialled is counted here.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="detail">How it ended.</param>
    public void RefusedBeforeHello(PeerSession session, string detail)
    {
        Pending? backedOff = null;
        if (!session.IsDialledHere)
        {
            lock (gate)
            {
                DateTimeOffset now = time.GetUtcNow();
                if (!stopped)
                    backedOff = Fail(EntryOf(session.Device), true, detail, now);
                Publish(now);
            }
        }

        facts.Record(NodeFactKind.RefusedBeforeHello, session.Connection, session.Remote, session.Device, detail);
        Record(backedOff);
    }

    /// <summary>The device broke the protocol (SPEC.md §12.1): counted, and three within an hour hold dials back for an hour.</summary>
    /// <param name="device">The device.</param>
    /// <param name="connection">The connection it happened on.</param>
    /// <param name="detail">What it did.</param>
    public void Violation(DeviceId device, long connection, string detail)
    {
        Pending? backedOff = null;
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            Entry entry = EntryOf(device);
            entry.Violations = entry.Violations.Add(now);
            TimeSpan hold = Backoff.ForViolations(entry.Violations.Count);
            if (hold > TimeSpan.Zero)
            {
                entry.ViolatedUntil = now + hold;
                backedOff = new Pending(NodeFactKind.BackedOff, connection, null, device, $"{entry.Violations.Count} violations within an hour; no dial until {entry.ViolatedUntil:O}");
                ScheduleRetry(entry, hold);
            }

            Publish(now);
        }

        facts.Record(NodeFactKind.Violation, connection, null, device, detail);
        Record(backedOff);
    }

    /// <summary>
    /// The device sent faulty content (SPEC.md §12.2): well-formed on the wire, wrong in substance. Not a violation; the
    /// session goes on, the content is dropped and never forwarded, and the device is recorded as faulty for it.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="connection">The connection it came on.</param>
    /// <param name="hash">The faulty object's hash.</param>
    /// <param name="detail">What is wrong with it.</param>
    public void Faulty(DeviceId device, long connection, ObjectHash hash, string detail)
    {
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            EntryOf(device).Faulty++;
            Publish(now);
        }

        facts.Record(NodeFactKind.FaultyContent, connection, null, device, hash.Value, detail);
    }

    /// <summary>A session ended, established or not.</summary>
    /// <param name="session">The session.</param>
    /// <param name="detail">Why it ended, for the facts.</param>
    public void SessionEnded(PeerSession session, string detail)
    {
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            if (devices.TryGetValue(session.Device, out Entry? entry))
            {
                entry.Sessions.Remove(session);
                if (ReferenceEquals(entry.Established, session))
                    entry.Established = null;
                entry.FirstSeen ??= now;
                entry.LastSeen = now;
            }

            Publish(now);
        }

        facts.Record(NodeFactKind.SessionEnded, session.Connection, session.Remote, session.Device, detail);
    }

    /// <summary>The sessions whose device the given trust no longer lists, not removed, in a known user's Roster (SPEC.md §4.3, §8.8).</summary>
    /// <param name="current">The trust that applies now.</param>
    /// <returns>The sessions to close with <c>Bye(Unauthorized)</c>.</returns>
    public IReadOnlyList<PeerSession> Untrusted(TrustSnapshot current)
    {
        lock (gate)
        {
            return devices.Values
                .SelectMany(e => e.Sessions)
                .Where(s => !current.TryResolveDevice(s.Peer.Key.Encoded, out _, out _))
                .ToList();
        }
    }

    /// <summary>When an address was last dialled and how that ended (LOCAL-SPEC.md §5.9 <c>StaticPeer</c>).</summary>
    /// <param name="address">The address.</param>
    /// <returns>The time of the last dial (<see langword="null"/> for never) and its result.</returns>
    public (DateTimeOffset? LastDialled, DialResult LastResult) StaticStatus(IPEndPoint address)
    {
        lock (gate)
            return statics.TryGetValue(address, out StaticEntry? entry) ? (entry.LastDialled, entry.LastResult) : (null, DialResult.NeverDialled);
    }

    /// <summary>
    /// Dials an address once at the user's request (LOCAL-SPEC.md §6.6 <c>Dial</c>), whatever device answers, under the
    /// backoff of SPEC.md §4.4; a failure is not retried unless the address is a configured static peer.
    /// </summary>
    /// <param name="address">The address.</param>
    public void DialAddress(IPEndPoint address)
    {
        ArgumentNullException.ThrowIfNull(address);
        DialRequest? request;
        lock (gate)
        {
            if (stopped)
                return;
            if (!statics.ContainsKey(address))
                statics[address] = new StaticEntry(address) { OneOff = true };
            DateTimeOffset now = time.GetUtcNow();
            request = OfferStatic(address, now);
            Publish(now);
        }

        if (request is not null)
            requests.Writer.TryWrite(request);
    }

    /// <summary>
    /// Dials a known device at the user's request (LOCAL-SPEC.md §6.6 <c>Dial</c>), at the given address or the most recent
    /// one discovery reported, under the backoff of SPEC.md §4.4.
    /// </summary>
    /// <param name="device">The device; the caller checked it is known and not established.</param>
    /// <param name="user">Its user.</param>
    /// <param name="address">The address; <see langword="null"/> for the one discovery knows.</param>
    /// <returns><see langword="false"/> when no address is known for the device.</returns>
    public bool DialDevice(DeviceId device, UserId user, IPEndPoint? address)
    {
        DialRequest? request = null;
        lock (gate)
        {
            if (stopped)
                return true;
            DateTimeOffset now = time.GetUtcNow();
            Entry entry = EntryOf(device);
            entry.User = user;
            if (address is not null)
                entry.AddAddress(address);
            if (entry.Addresses.Count == 0)
                return false;
            IPEndPoint target = address ?? entry.Addresses[0];
            bool backedOff = entry.BackoffUntil is { } until && until > now;
            if (!backedOff && !entry.Dialling && entry.Established is null && !attempts.ContainsKey(target))
            {
                entry.Dialling = true;
                request = new DialRequest(target, device);
                attempts[target] = request;
            }

            Publish(now);
        }

        if (request is not null)
            requests.Writer.TryWrite(request);
        return true;
    }

    /// <summary>
    /// Dials a device a pairing just made known (PAIR-SPEC.md §9), at the address the pairing reached: a backoff from
    /// before the pairing, when the device was still unknown and refused, no longer holds.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="user">Its user.</param>
    /// <param name="address">Its address.</param>
    public void DialPaired(DeviceId device, UserId user, IPEndPoint address)
    {
        lock (gate)
        {
            if (devices.TryGetValue(device, out Entry? entry))
            {
                entry.Failures = 0;
                entry.FailedUntil = null;
                entry.Refused = false;
                entry.Retry?.Dispose();
                entry.Retry = null;
            }
        }

        DialDevice(device, user, address);
    }

    /// <summary>A static peer was removed: its address is no longer retried.</summary>
    /// <param name="address">The address.</param>
    public void ForgetStatic(IPEndPoint address)
    {
        lock (gate)
        {
            if (statics.Remove(address, out StaticEntry? entry))
                entry.Retry?.Dispose();
        }
    }

    /// <summary>The established session to a device, if one is.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The session, or <see langword="null"/>.</returns>
    public PeerSession? EstablishedWith(DeviceId device)
    {
        lock (gate)
            return devices.TryGetValue(device, out Entry? entry) ? entry.Established : null;
    }

    /// <summary>The record of one device, if the table holds it.</summary>
    /// <param name="device">The device.</param>
    /// <returns>Its record, or <see langword="null"/>.</returns>
    public PeerRecord? Find(DeviceId device) => View.FirstOrDefault(r => r.Device == device);

    /// <summary>Stops deciding: no more dials, every retry timer disposed.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            stopped = true;
            foreach (Entry entry in devices.Values)
                entry.Retry?.Dispose();
            foreach (StaticEntry entry in statics.Values)
                entry.Retry?.Dispose();
        }

        requests.Writer.TryComplete();
    }

    private DialRequest? OfferDevice(Announcement announcement, DateTimeOffset now)
    {
        TrustSnapshot current = trust();
        devices.TryGetValue(announcement.Device, out Entry? entry);
        DialDecision decision = DialPolicy.Decide(announcement, current, current.Self?.Device, entry?.StateAt(now) ?? PeerState.Discovered, entry?.BackoffUntil, now);
        if (decision.Reason is SkipReason.NoIdentity or SkipReason.Self or SkipReason.UnknownDevice or SkipReason.Removed)
            return null;

        entry ??= EntryOf(announcement.Device);
        entry.User = announcement.User;
        entry.Sources |= announcement.Source;
        entry.FirstSeen ??= now;
        entry.LastSeen = now;
        entry.AddAddress(announcement.Address);
        if (!decision.ShouldDial || attempts.ContainsKey(announcement.Address))
            return null;

        entry.Dialling = true;
        var request = new DialRequest(announcement.Address, announcement.Device);
        attempts[announcement.Address] = request;
        return request;
    }

    private DialRequest? OfferStatic(IPEndPoint address, DateTimeOffset now)
    {
        if (!statics.TryGetValue(address, out StaticEntry? entry))
            statics[address] = entry = new StaticEntry(address);
        PeerState state = entry.Dialling ? PeerState.Dialling
            : entry.Device is { } device && devices.TryGetValue(device, out Entry? reached) && reached.Sessions.Count > 0 ? PeerState.Connected
            : PeerState.Discovered;
        DialDecision decision = DialPolicy.DecideStatic(trust().Self is not null, state, entry.BackoffUntil, now);
        if (!decision.ShouldDial || attempts.ContainsKey(address))
            return null;

        entry.Dialling = true;
        entry.LastDialled = now;
        var request = new DialRequest(address, null);
        attempts[address] = request;
        return request;
    }

    private Pending Fail(Entry entry, bool refused, string detail, DateTimeOffset now)
    {
        entry.Failures++;
        entry.Refused = refused;
        TimeSpan wait = Backoff.Next(entry.Failures);
        entry.FailedUntil = now + wait;
        ScheduleRetry(entry, entry.BackoffUntil!.Value - now);
        return new Pending(NodeFactKind.BackedOff, 0, entry.Addresses.FirstOrDefault(), entry.Device, $"{(refused ? "refused" : "dial failed")} {entry.Failures} in a row ({detail}); no dial for {wait.TotalSeconds:0} s");
    }

    private Pending Fail(StaticEntry entry, string detail, DateTimeOffset now)
    {
        entry.Failures++;
        TimeSpan wait = Backoff.Next(entry.Failures);
        entry.BackoffUntil = now + wait;
        entry.Retry?.Dispose();
        entry.Retry = entry.OneOff ? null : time.CreateTimer(static state => ((Retry)state!).Fire(), new Retry(this, Announcement.ForStaticPeer(entry.Address)), wait, Timeout.InfiniteTimeSpan);
        return new Pending(NodeFactKind.BackedOff, 0, entry.Address, default, $"static peer, {entry.Failures} in a row ({detail}); no dial for {wait.TotalSeconds:0} s");
    }

    // Facts are recorded after the lock is left and the view published, so whoever waits on one reads the state it reports.
    private void Record(Pending? pending)
    {
        if (pending is { } fact)
            facts.Record(fact.Kind, fact.Connection, fact.Remote, fact.Device, fact.Detail);
    }

    // When the backoff ends, the device is offered again at its most recent address, as if announced: SPEC.md §4.4
    // dials a device discovery names, and a device that failed is still named.
    private void ScheduleRetry(Entry entry, TimeSpan wait)
    {
        entry.Retry?.Dispose();
        entry.Retry = null;
        if (entry.Addresses.Count == 0 || entry.Sources == DiscoverySource.None)
            return;
        var announcement = new Announcement(entry.Device, entry.User, entry.Addresses[0], entry.Sources & ~DiscoverySource.Static);
        entry.Retry = time.CreateTimer(static state => ((Retry)state!).Fire(), new Retry(this, announcement), wait, Timeout.InfiniteTimeSpan);
    }

    private Entry EntryOf(DeviceId device)
    {
        if (!devices.TryGetValue(device, out Entry? entry))
            devices[device] = entry = new Entry(device);
        return entry;
    }

    private void Publish(DateTimeOffset now)
    {
        PeerRecord[] next = devices.Values.Select(e => e.Record(now)).ToArray();
        Volatile.Write(ref view, next);
        changed?.Invoke(next);
    }

    private readonly record struct Pending(NodeFactKind Kind, long Connection, IPEndPoint? Remote, DeviceId Device, string Detail);

    private sealed class Retry(PeerTable table, Announcement announcement)
    {
        public void Fire() => table.Offer(announcement);
    }

    private sealed class Entry(DeviceId device)
    {
        public DeviceId Device { get; } = device;

        public UserId User { get; set; }

        public List<IPEndPoint> Addresses { get; } = [];

        public DiscoverySource Sources { get; set; }

        public DateTimeOffset? FirstSeen { get; set; }

        public DateTimeOffset? LastSeen { get; set; }

        public int Failures { get; set; }

        public bool Refused { get; set; }

        public DateTimeOffset? FailedUntil { get; set; }

        public ViolationWindow Violations { get; set; }

        public int Faulty { get; set; }

        public DateTimeOffset? ViolatedUntil { get; set; }

        public bool Dialling { get; set; }

        public List<PeerSession> Sessions { get; } = [];

        public PeerSession? Established { get; set; }

        public ITimer? Retry { get; set; }

        public DateTimeOffset? BackoffUntil => FailedUntil is { } failed && ViolatedUntil is { } violated
            ? (failed > violated ? failed : violated)
            : FailedUntil ?? ViolatedUntil;

        public void AddAddress(IPEndPoint address)
        {
            Addresses.Remove(address);
            Addresses.Insert(0, address);
            if (Addresses.Count > MaxAddresses)
                Addresses.RemoveAt(MaxAddresses);
        }

        public PeerState StateAt(DateTimeOffset now) =>
            Established is not null ? PeerState.Established
            : Sessions.Count > 0 ? PeerState.Connected
            : Dialling ? PeerState.Dialling
            : ViolatedUntil is { } violated && now < violated ? PeerState.BackedOff
            : FailedUntil is { } failed && now < failed ? (Refused ? PeerState.Refused : PeerState.BackedOff)
            : PeerState.Discovered;

        public PeerRecord Record(DateTimeOffset now) => new()
        {
            Device = Device,
            User = User,
            State = StateAt(now),
            Addresses = Addresses.ToArray(),
            Sources = Sources,
            FirstSeen = FirstSeen,
            LastSeen = LastSeen,
            BackoffUntil = BackoffUntil is { } until && until > now ? until : null,
            Failures = Failures,
            Violations = Violations.Count != 0 && now - Violations.Since < TimeSpan.FromSeconds(Limits.ViolationWindowSeconds) ? Violations.Count : 0,
            Sessions = Sessions.Count,
            Faulty = Faulty,
        };
    }

    private sealed class StaticEntry(IPEndPoint address)
    {
        public IPEndPoint Address { get; } = address;

        public bool OneOff { get; set; }

        public DateTimeOffset? LastDialled { get; set; }

        public DialResult LastResult { get; set; }

        public bool Dialling { get; set; }

        public int Failures { get; set; }

        public DateTimeOffset? BackoffUntil { get; set; }

        public DeviceId? Device { get; set; }

        public ITimer? Retry { get; set; }
    }
}
