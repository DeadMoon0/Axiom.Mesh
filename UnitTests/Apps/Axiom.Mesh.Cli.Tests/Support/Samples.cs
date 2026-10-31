using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Cli.Tests.Support;

/// <summary>Fixed records of LOCAL-SPEC.md §5 for the output snapshots: every id and time chosen, nothing random.</summary>
internal static class Samples
{
    /// <summary>A time: 2027-01-15 08:00:00Z.</summary>
    public const ulong Time = 1_800_000_000_000;

    public static UserId Alice { get; } = new(Id(0xa1));

    public static UserId Bob { get; } = new(Id(0xb0));

    public static DeviceId Laptop { get; } = new(Id(0x1a));

    public static DeviceId Desktop { get; } = new(Id(0xd3));

    public static PackageId Chat { get; } = new(Id(0xc4));

    public static PackageId Photos { get; } = new(Id(0x9f));

    public static Identity Identity { get; } = new()
    {
        Present = true,
        UserId = Alice,
        UserName = "alice",
        UserKeyPresent = true,
        DeviceId = Laptop,
        DeviceName = "laptop",
        RosterSeq = 2,
        Created = Time,
    };

    public static HelloAck Ack { get; } = new() { HostVersion = "0.1.0", Role = Role.User, Identity = Identity, StoreEpoch = default, StoreSeq = 7 };

    public static Store Store { get; } = new() { Path = string.Empty, Objects = 12, Bytes = 3_500_000, Budget = 0, PinnedBytes = 1_048_576, EvictableBytes = 2_451_424, HeldBytes = 0 };

    public static IReadOnlyList<Peer> Peers { get; } =
    [
        new Peer { DeviceId = Desktop, UserId = Bob, Known = true, Addresses = ["192.168.1.20:41810"], Sources = PeerSources.Mdns, FirstSeen = Time, LastSeen = Time + 60_000, State = PeerState.Established, BackoffUntil = 0, Violations = 0 },
        new Peer { DeviceId = new DeviceId(Id(0xee)), UserId = default, Known = false, Addresses = [], Sources = PeerSources.Broadcast, FirstSeen = Time, LastSeen = Time, State = PeerState.Unknown, BackoffUntil = 0, Violations = 1 },
    ];

    public static IReadOnlyList<Package> Packages { get; } =
    [
        Package(Chat, "Chat", PackageKind.Channel, Alice, PackageState.Followed, GrantRights.Write, 3),
        Package(Photos, "Photos", PackageKind.Archive, Bob, PackageState.Listed, GrantRights.Read, 0),
    ];

    public static IReadOnlyList<Entry> History { get; } =
    [
        Entry(0x01, 0, Laptop, EntryKind.Message, PayloadEncoder.Encode(new MessagePayload { Text = "hello mesh", Attachments = [], ReplyTo = default }), redacted: false),
        Entry(0x02, 1, Desktop, EntryKind.Message, PayloadEncoder.Encode(new MessagePayload { Text = "a secret", Attachments = [], ReplyTo = default }), redacted: true),
        Entry(0x03, 2, Laptop, EntryKind.Redaction, PayloadEncoder.Encode(new RedactionPayload { Target = new ObjectHash(Id(0x02)) }), redacted: false) with { Redacts = new ObjectHash(Id(0x02)) },
    ];

    private static Hash32 Id(byte first) => new([first, .. Enumerable.Range(1, 31).Select(i => (byte)(first ^ i))]);

    private static Package Package(PackageId id, string name, PackageKind kind, UserId owner, PackageState state, GrantRights rights, ulong entries) => new()
    {
        PackageId = id,
        State = state,
        OwnerId = owner,
        Kind = kind,
        Name = name,
        Created = Time,
        Retention = Retention.Default,
        ManifestSeq = 1,
        ManifestHash = new ObjectHash(Id(0x4d)),
        Branches = [],
        ListedBy = 1,
        Policy = LocalPolicy.Default,
        Rights = rights,
        Heads = [],
        Entries = entries,
        LatestClock = entries == 0 ? 0 : entries - 1,
    };

    private static Entry Entry(byte hash, ulong clock, DeviceId device, EntryKind kind, byte[] payload, bool redacted) => new()
    {
        Hash = new ObjectHash(Id(hash)),
        StoreSeq = clock + 1,
        Stored = Time + (clock * 1_000),
        PackageId = Chat,
        DeviceId = device,
        Clock = clock,
        Parents = clock == 0 ? [] : [new ObjectHash(Id((byte)(hash - 1)))],
        Kind = kind,
        Payload = payload,
        Redacted = redacted,
    };
}
