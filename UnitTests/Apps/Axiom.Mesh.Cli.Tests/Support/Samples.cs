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

    public static IReadOnlyList<Local.Service> Services { get; } =
    [
        new Local.Service { Name = "chat", Connected = true, InProcess = false, Version = "0.2.0", Spawned = true, Kinds = [PackageKind.Channel], EntryKinds = [EntryKind.Message, EntryKind.Redaction] },
        new Local.Service { Name = "cli", Connected = false, InProcess = false, Version = "", Spawned = false, Kinds = [PackageKind.Archive, PackageKind.Channel], EntryKinds = [EntryKind.Version, EntryKind.Message, EntryKind.Redaction] },
        new Local.Service { Name = "sync", Connected = false, InProcess = false, Version = "", Spawned = true, Kinds = [PackageKind.Archive], EntryKinds = [EntryKind.Version] },
    ];

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

    /// <summary>Two Channels as the chat service lists them.</summary>
    public static IReadOnlyList<Chat.ChannelView> Channels { get; } =
    [
        new Chat.ChannelView { PackageId = Chat, Name = "Family", OwnerId = Alice, OwnerName = "alice", Followed = true, Messages = 3, Withdrawn = 1, Last = new EntryKey(2, Desktop, new ObjectHash(Id(0x03))), Indexed = true },
        new Chat.ChannelView { PackageId = new PackageId(Id(0xc7)), Name = "Work", OwnerId = Bob, OwnerName = string.Empty, Followed = false, Messages = 0, Withdrawn = 0, Last = null, Indexed = false },
    ];

    /// <summary>A timeline: a message with two attachments, a withdrawn one, a reply.</summary>
    public static IReadOnlyList<Chat.MessageView> Timeline { get; } =
    [
        new Chat.MessageView
        {
            Key = new EntryKey(0, Laptop, new ObjectHash(Id(0x01))),
            PackageId = Chat,
            Author = new Chat.AuthorView { DeviceId = Laptop, UserId = Alice, UserName = "alice", DeviceName = "laptop" },
            Stored = Time,
            Text = "holiday photos",
            Attachments = [new Attachment("photo.jpg", new ObjectHash(Id(0x9a)), 1_048_576), new Attachment("docs", new ObjectHash(Id(0x9b)), 3_072)],
        },
        new Chat.MessageView
        {
            Key = new EntryKey(1, Desktop, new ObjectHash(Id(0x02))),
            PackageId = Chat,
            Author = new Chat.AuthorView { DeviceId = Desktop, UserId = Bob, UserName = "bob", DeviceName = "desktop" },
            Stored = Time + 1_000,
            Withdrawn = true,
            WithdrawnBy = Laptop,
        },
        new Chat.MessageView
        {
            Key = new EntryKey(2, Desktop, new ObjectHash(Id(0x03))),
            PackageId = Chat,
            Author = new Chat.AuthorView { DeviceId = Desktop },
            Stored = Time + 2_000,
            Text = "lovely",
            ReplyTo = new ObjectHash(Id(0x01)),
        },
    ];

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
    /// <summary>Two bindings as the sync service lists them: one in sync, one read-only with local changes and a problem.</summary>
    public static IReadOnlyList<Sync.Binding> Bindings { get; } =
    [
        new Sync.Binding
        {
            Directory = "/home/alice/shared",
            PackageId = Photos,
            Label = "main",
            Symlinks = Sync.SymlinkPolicy.AsFile,
            Ignore = ["node_modules", "*.tmp"],
            State = Sync.BindingState.InSync,
            Rights = GrantRights.Write,
            Base = new ObjectHash(Id(0x31)),
            Head = new ObjectHash(Id(0x32)),
            Leaves = 1,
            Files = 1_204,
            Bytes = 734_003_200,
            LastRun = Time,
            Runs = 17,
        },
        new Sync.Binding
        {
            Directory = "/home/alice/music",
            PackageId = Chat,
            Label = "main",
            Symlinks = Sync.SymlinkPolicy.Ignore,
            State = Sync.BindingState.LocalChangesReadOnly,
            Rights = GrantRights.Read,
            Base = new ObjectHash(Id(0x41)),
            Head = new ObjectHash(Id(0x42)),
            Leaves = 2,
            Files = 12,
            Bytes = 52_428_800,
            LocalChanges = 2,
            Conflicts = 1,
            LastRun = Time + 60_000,
            Runs = 3,
            Pending = true,
            Error = "two files changed here are not published: this device may only read",
        },
    ];

    /// <summary>A conflict as the sync service records it.</summary>
    public static IReadOnlyList<Sync.Conflict> Conflicts { get; } =
    [
        new Sync.Conflict
        {
            Id = 1,
            Directory = "/home/alice/shared",
            Path = "docs/report.txt",
            Kind = Sync.ConflictKind.CopyWritten,
            CopyPath = "docs/report.conflict-d3d2d1d0.txt",
            OtherDevice = Desktop,
            Version = new ObjectHash(Id(0x33)),
            At = Time,
        },
    ];
}
