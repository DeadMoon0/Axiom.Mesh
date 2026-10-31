using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Axiom.Mesh.Discovery;
using Axiom.Mesh.Local;
using LocalPeerState = Axiom.Mesh.Local.PeerState;

namespace Axiom.Mesh.Core;

/// <summary>
/// The records of LOCAL-SPEC.md §5 as functions of what the node holds: trust, the peer table's view, replication's
/// counters, the store's totals and the configuration. Every list comes out in the order its section states.
/// </summary>
internal static class LocalRecords
{
    /// <summary>The <c>Identity</c> record (§5.1).</summary>
    /// <param name="trust">The trust that holds now.</param>
    /// <param name="userKeyHeld">Whether the user's private key is on this device.</param>
    /// <returns>The record; not present before an identity exists.</returns>
    public static Identity IdentityOf(TrustSnapshot trust, bool userKeyHeld)
    {
        if (trust.Self is not { } self)
            return new Identity();

        RosterDevice? device = null;
        ulong rosterSeq = 0;
        if (trust.TryGetUser(self.User, out UserTrust? user) && user.Roster is { } roster)
        {
            rosterSeq = roster.Content.Seq;
            roster.Content.TryGetDevice(self.Device, out device);
        }

        return new Identity
        {
            Present = true,
            UserId = self.User,
            UserName = self.UserName,
            UserKeyPresent = userKeyHeld,
            DeviceId = self.Device,
            DeviceName = device?.Name ?? string.Empty,
            RosterSeq = rosterSeq,
            Created = Milliseconds(self.CreatedMs),
        };
    }

    /// <summary>The <c>Device</c> record (§5.3) of a device in a user's current Roster.</summary>
    /// <param name="device">The device as the Roster and the removal rule leave it.</param>
    /// <param name="user">Its user.</param>
    /// <param name="self">This device.</param>
    /// <param name="lastSeen">The end of the last established session with it; <see langword="null"/> when never.</param>
    /// <returns>The record.</returns>
    public static Device DeviceOf(EffectiveDevice device, UserId user, DeviceId? self, DateTimeOffset? lastSeen) => new()
    {
        DeviceId = device.Device.Id,
        UserId = user,
        Name = device.Device.Name,
        AddedAt = device.Device.AddedAt,
        Removed = device.IsRemoved,
        Self = self is { } own && own == device.Device.Id,
        LastSeen = Milliseconds(lastSeen),
    };

    /// <summary>The devices of a user's current Roster, ascending by device id (§6.3, §7.3 <c>UserChanged</c>).</summary>
    /// <param name="user">The user.</param>
    /// <param name="self">This device.</param>
    /// <param name="peers">The peer table's view, for when each was last seen.</param>
    /// <returns>The records.</returns>
    public static IReadOnlyList<Device> DevicesOf(UserTrust user, DeviceId? self, IReadOnlyList<PeerRecord> peers) =>
        [.. user.Devices
            .Select(d => DeviceOf(d, user.Id, self, peers.FirstOrDefault(p => p.Device == d.Device.Id)?.LastSeen))
            .OrderBy(d => d.DeviceId.Value)];

    /// <summary>The <c>User</c> record (§5.2).</summary>
    /// <param name="user">The user's trust.</param>
    /// <param name="self">The own user.</param>
    /// <param name="knownSinceMs">When the user became known, in milliseconds since the Unix epoch.</param>
    /// <param name="name">The display name this device keeps for the user.</param>
    /// <returns>The record.</returns>
    public static User UserOf(UserTrust user, UserId? self, long knownSinceMs, string name) => new()
    {
        UserId = user.Id,
        Name = name,
        Self = self is { } own && own == user.Id,
        KnownSince = Milliseconds(knownSinceMs),
        RosterSeq = user.Roster?.Content.Seq ?? 0,
        Devices = (ushort)Math.Min(ushort.MaxValue, user.Devices.Count(d => !d.IsRemoved)),
    };

    /// <summary>The <c>Peer</c> record (§5.7).</summary>
    /// <param name="peer">The peer table's view of the device.</param>
    /// <param name="trust">The trust that holds now, for <c>known</c>.</param>
    /// <returns>The record.</returns>
    public static Peer PeerOf(PeerRecord peer, TrustSnapshot trust)
    {
        bool known = trust.TryGetDevice(peer.Device, out TrustedDevice device) && !device.IsRemoved;
        return new Peer
        {
            DeviceId = peer.Device,
            UserId = peer.User,
            Known = known,
            Addresses = [.. peer.Addresses.Take(8).Select(AddressOf).Order(StringComparer.Ordinal)],
            Sources = SourcesOf(peer.Sources, peer.Sessions > 0),
            FirstSeen = Milliseconds(peer.FirstSeen),
            LastSeen = Milliseconds(peer.LastSeen),
            State = known ? StateOf(peer.State) : LocalPeerState.Unknown,
            BackoffUntil = Milliseconds(peer.BackoffUntil),
            Violations = (uint)Math.Max(0, peer.Violations),
        };
    }

    /// <summary>A device discovery saw that the peer table does not hold (§6.6 <c>ListDiscovered</c>).</summary>
    /// <param name="heard">What discovery heard last.</param>
    /// <param name="trust">The trust that holds now.</param>
    /// <returns>The record.</returns>
    public static Peer PeerOf(Announcement heard, TrustSnapshot trust)
    {
        bool known = trust.TryGetDevice(heard.Device, out TrustedDevice device) && !device.IsRemoved;
        return new Peer
        {
            DeviceId = heard.Device,
            UserId = heard.User,
            Known = known,
            Addresses = [AddressOf(heard.Address)],
            Sources = SourcesOf(heard.Source, false),
            State = known ? LocalPeerState.Discovered : LocalPeerState.Unknown,
        };
    }

    /// <summary>The <c>Session</c> record (§5.8) of an established session.</summary>
    /// <param name="traffic">Replication's counters for it.</param>
    /// <param name="session">The peer table's session, for direction, address and suite; <see langword="null"/> when it ended meanwhile.</param>
    /// <param name="now">The node's clock now.</param>
    /// <returns>The record.</returns>
    public static Session SessionOf(SessionTraffic traffic, PeerSession? session, DateTimeOffset now) => new()
    {
        DeviceId = traffic.Device,
        UserId = traffic.User,
        Direction = session is null ? SessionDirection.Unassigned : session.IsDialledHere ? SessionDirection.Outbound : SessionDirection.Inbound,
        Remote = session is null ? string.Empty : AddressOf(session.Remote),
        Since = Milliseconds(traffic.Since),
        LastReceived = session is null ? 0 : Milliseconds(now - session.Activity.SilentFor()),
        TlsSuite = session?.Peer.CipherSuite.ToString() ?? string.Empty,
        State = SessionState.Established,
        BytesIn = (ulong)Math.Max(0, traffic.BytesIn),
        BytesOut = (ulong)Math.Max(0, traffic.BytesOut),
        ObjectsIn = (ulong)Math.Max(0, traffic.ObjectsIn),
        ObjectsOut = (ulong)Math.Max(0, traffic.ObjectsOut),
        WantsOut = (ushort)Math.Clamp(traffic.WantsOut, 0, ushort.MaxValue),
        WantsIn = (ushort)Math.Clamp(traffic.WantsIn, 0, ushort.MaxValue),
    };

    /// <summary>The <c>Store</c> record (§5.10).</summary>
    /// <param name="totals">The store's totals after the last batch.</param>
    /// <param name="path">The store directory; empty for a store in memory.</param>
    /// <param name="heldBytes">Bytes in the held buffer.</param>
    /// <returns>The record.</returns>
    public static Store StoreOf(StoreTotals totals, string? path, long heldBytes) => new()
    {
        Path = path ?? string.Empty,
        Objects = (ulong)Math.Max(0, totals.Objects),
        Bytes = (ulong)Math.Max(0, totals.Bytes),
        Budget = totals.Budget == long.MaxValue ? 0 : (ulong)Math.Max(0, totals.Budget),
        PinnedBytes = (ulong)Math.Max(0, totals.PinnedBytes),
        EvictableBytes = (ulong)Math.Max(0, totals.EvictableBytes),
        HeldBytes = (ulong)Math.Max(0, heldBytes),
    };

    /// <summary>The <c>Service</c> record (§5.12).</summary>
    /// <param name="service">The configured service.</param>
    /// <param name="connectedVersion">The <c>clientVersion</c> of its live connection; <see langword="null"/> when not connected.</param>
    /// <returns>The record.</returns>
    public static Service ServiceOf(LocalService service, string? connectedVersion) => new()
    {
        Name = service.Name,
        Connected = connectedVersion is not null,
        InProcess = service.Provenance == ServiceProvenance.InProcess,
        Version = connectedVersion ?? string.Empty,
        Spawned = service.Provenance == ServiceProvenance.Spawned,
        Kinds = [.. service.Kinds.Order()],
        EntryKinds = [.. service.EntryKinds.Order()],
    };

    /// <summary>The <c>Entry</c> record (§5.6) of a stored Entry.</summary>
    /// <param name="content">The Entry's decoded fields.</param>
    /// <param name="storeSeq">Its store sequence.</param>
    /// <param name="storedMs">When this Host stored it.</param>
    /// <param name="redacted">Whether a stored redaction withdraws it.</param>
    /// <returns>The record.</returns>
    public static Entry EntryOf(EntryContent content, ulong storeSeq, long storedMs, bool redacted) => new()
    {
        Hash = content.Hash,
        StoreSeq = storeSeq,
        Stored = Milliseconds(storedMs),
        PackageId = content.Package,
        DeviceId = content.Device,
        Clock = content.Clock,
        Parents = [.. content.Parents],
        Kind = content.Kind,
        References = [.. content.References],
        Redacts = content.Redacts,
        Payload = content.Payload,
        Redacted = redacted,
    };

    /// <summary>The <c>Grant</c> record (§5.5).</summary>
    /// <param name="grant">The verified Grant.</param>
    /// <returns>The record.</returns>
    public static Grant GrantOf(VerifiedGrant grant) => new()
    {
        Hash = grant.Content.Hash,
        PackageId = grant.Content.Package,
        GranteeId = grant.Content.Grantee,
        Rights = grant.Content.Rights,
        Seq = grant.Content.Seq,
    };

    /// <summary>A time as LOCAL-SPEC.md writes it: milliseconds since the Unix epoch, 0 for unknown (§3.2).</summary>
    /// <param name="at">The time.</param>
    /// <returns>The milliseconds.</returns>
    public static ulong Milliseconds(DateTimeOffset? at) => at is { } value ? Milliseconds(value.ToUnixTimeMilliseconds()) : 0;

    /// <summary>A count of milliseconds since the Unix epoch as a <c>time</c>; a negative one is unknown.</summary>
    /// <param name="milliseconds">The milliseconds.</param>
    /// <returns>The time.</returns>
    public static ulong Milliseconds(long milliseconds) => milliseconds <= 0 ? 0 : (ulong)milliseconds;

    private static string AddressOf(IPEndPoint address) => address.ToString();

    private static PeerSources SourcesOf(DiscoverySource sources, bool session) =>
        ((sources & DiscoverySource.Mdns) != 0 ? PeerSources.Mdns : PeerSources.None)
        | ((sources & DiscoverySource.Broadcast) != 0 ? PeerSources.Broadcast : PeerSources.None)
        | ((sources & DiscoverySource.Static) != 0 ? PeerSources.Static : PeerSources.None)
        | (session ? PeerSources.Session : PeerSources.None);

    private static LocalPeerState StateOf(PeerState state) => state switch
    {
        PeerState.Discovered => LocalPeerState.Discovered,
        PeerState.Dialling or PeerState.Connected => LocalPeerState.Dialling,
        PeerState.Established => LocalPeerState.Established,
        PeerState.Refused => LocalPeerState.Refused,
        PeerState.BackedOff => LocalPeerState.Backoff,
        _ => LocalPeerState.Discovered,
    };
}
