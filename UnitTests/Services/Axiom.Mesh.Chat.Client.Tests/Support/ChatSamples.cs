using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat.Client.Tests.Support;

/// <summary>One fixed, non-trivial sample of every record of <c>axiom-mesh-chat/1</c>, and their encoding by reflection.</summary>
internal static class ChatSamples
{
    private static readonly AuthorView Author = new() { DeviceId = new DeviceId(Id(0xd3)), UserId = new UserId(Id(0xb0)), UserName = "bob", DeviceName = "desktop" };

    private static readonly EntryKey Key = new(7, new DeviceId(Id(0xd3)), new ObjectHash(Id(0x71)));

    private static readonly Attachment Photo = new("photo.jpg", new ObjectHash(Id(0x9a)), 1_048_576);

    private static readonly MessageView Message = new()
    {
        Key = Key,
        PackageId = new PackageId(Id(0xc4)),
        Author = Author,
        Stored = 1_800_000_000_000,
        Text = "dinner at eight? éè",
        Attachments = [Photo, new Attachment("docs", new ObjectHash(Id(0x9b)), 23_000)],
        ReplyTo = new ObjectHash(Id(0x70)),
        Withdrawn = false,
        WithdrawnBy = default,
        Broken = false,
    };

    private static readonly ChannelView Channel = new()
    {
        PackageId = new PackageId(Id(0xc4)),
        Name = "Family",
        OwnerId = new UserId(Id(0xa1)),
        OwnerName = "alice",
        Followed = true,
        Messages = 12,
        Withdrawn = 2,
        Last = Key,
        Indexed = true,
    };

    private static readonly JobView Job = new()
    {
        Job = 3,
        Kind = JobKind.Get,
        State = JobState.Failed,
        Done = 100,
        Total = 1_048_576,
        Message = new ObjectHash(Id(0x71)),
        Status = 3,
        Attachment = Photo,
        Path = "/home/bob/photo.jpg",
        Problem = "No peer supplied the attachment.",
    };

    /// <summary>Every record type of the Client library, by name.</summary>
    public static IReadOnlyList<Type> Types() =>
        [.. typeof(ChatCall).Assembly.GetExportedTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IWireRecord<>)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)];

    /// <summary>The sample of a record type.</summary>
    public static object Of(Type type) => type.Name switch
    {
        nameof(AboutRequest) => new AboutRequest(),
        nameof(AboutReply) => new AboutReply { Namespace = ChatProtocol.Namespace, Version = "0.2.0", StoreEpoch = Id(0xe1), IndexedSeq = 4_242, CaughtUp = true, Channels = 3 },
        nameof(ChannelsRequest) => new ChannelsRequest(),
        nameof(ChannelsReply) => new ChannelsReply { Channels = [Channel, Channel with { Name = "Work", Last = null, Indexed = false }] },
        nameof(TimelineRequest) => new TimelineRequest { PackageId = new PackageId(Id(0xc4)), After = Key, Reverse = true, Limit = 50 },
        nameof(TimelineReply) => new TimelineReply { Messages = [Message, Message with { Withdrawn = true, Text = string.Empty, Attachments = [], WithdrawnBy = new DeviceId(Id(0x1a)) }], More = true },
        nameof(MessageRequest) => new MessageRequest { Hash = new ObjectHash(Id(0x71)) },
        nameof(MessageReply) => new MessageReply { Message = Message },
        nameof(ThreadRequest) => new ThreadRequest { Message = new ObjectHash(Id(0x71)), Limit = 20 },
        nameof(ThreadReply) => new ThreadReply { Root = Message, Replies = [Message with { ReplyTo = new ObjectHash(Id(0x71)) }], More = false },
        nameof(SendRequest) => new SendRequest { PackageId = new PackageId(Id(0xc4)), Text = "hello", Attachments = [Photo], ReplyTo = new ObjectHash(Id(0x70)) },
        nameof(SendReply) => new SendReply { Key = Key },
        nameof(WithdrawRequest) => new WithdrawRequest { Message = new ObjectHash(Id(0x71)) },
        nameof(WithdrawReply) => new WithdrawReply { Key = Key with { Clock = 8 } },
        nameof(AttachRequest) => new AttachRequest { Path = "/home/alice/photo.jpg", Name = "photo.jpg" },
        nameof(AttachReply) => new AttachReply { Job = 2 },
        nameof(GetRequest) => new GetRequest { Hash = Photo.Hash, Message = new ObjectHash(Id(0x71)), Path = "/home/bob/photo.jpg", Existing = ExistingPath.Replace },
        nameof(GetReply) => new GetReply { Job = 3 },
        nameof(JobsRequest) => new JobsRequest(),
        nameof(JobsReply) => new JobsReply { Jobs = [Job, Job with { Job = 4, Kind = JobKind.Attach, State = JobState.Running, Attachment = null, Problem = string.Empty, Status = 0 }] },
        nameof(AuthorView) => Author,
        nameof(ChannelView) => Channel,
        nameof(MessageView) => Message,
        nameof(JobView) => Job,
        nameof(MessageEvent) => new MessageEvent { Key = Key, PackageId = new PackageId(Id(0xc4)), Author = Author, Preview = "dinner at eight?", Attachments = 2, ReplyTo = new ObjectHash(Id(0x70)) },
        nameof(WithdrawnEvent) => new WithdrawnEvent { Key = Key, PackageId = new PackageId(Id(0xc4)), By = new DeviceId(Id(0x1a)) },
        nameof(ChannelEvent) => new ChannelEvent { Change = ChannelChange.Renamed, Channel = Channel },
        nameof(JobEvent) => new JobEvent { Job = Job },
        _ => throw new ArgumentException("No sample of " + type.Name, nameof(type)),
    };

    /// <summary>A record's encoding, through <see cref="ChatCodec.Encode{T}"/>.</summary>
    public static byte[] Encode(object record) =>
        (byte[])typeof(ChatCodec).GetMethod(nameof(ChatCodec.Encode))!.MakeGenericMethod(record.GetType()).Invoke(null, [record])!;

    /// <summary>Reads a payload as a record type, through <see cref="ChatCodec.TryDecode{T}"/>.</summary>
    public static bool TryDecode(Type type, byte[] payload, out object? record)
    {
        object?[] arguments = [new ReadOnlyMemory<byte>(payload), null];
        bool read = (bool)typeof(ChatCodec).GetMethod(nameof(ChatCodec.TryDecode))!.MakeGenericMethod(type).Invoke(null, arguments)!;
        record = arguments[1];
        return read;
    }

    /// <summary>Whether a prefix of a payload reads as the type, its reader at the prefix's end (it must not).</summary>
    public static bool PrefixReads(Type type, byte[] prefix) =>
        (bool)typeof(ChatSamples).GetMethod(nameof(PrefixReadsOf), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, [prefix])!;

    public static Hash32 Id(byte first) => new([first, .. Enumerable.Range(1, 31).Select(i => (byte)(first ^ (i * 3)))]);

    private static bool PrefixReadsOf<T>(byte[] prefix)
        where T : class, IWireRecord<T>
    {
        var reader = new WireReader(prefix);
        return T.TryRead(ref reader, out _) && reader.Ok && reader.IsAtEnd;
    }

}
