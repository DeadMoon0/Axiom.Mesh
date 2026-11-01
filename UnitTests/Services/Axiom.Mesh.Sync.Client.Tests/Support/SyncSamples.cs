using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync.Client.Tests.Support;

/// <summary>One fixed, non-trivial sample of every record of <c>axiom-mesh-sync/1</c>, and their encoding by reflection.</summary>
internal static class SyncSamples
{
    private static readonly Binding Binding = new()
    {
        Directory = "/home/alice/shared",
        PackageId = new PackageId(Id(0xa7)),
        Label = "main",
        Symlinks = SymlinkPolicy.AsFile,
        Ignore = ["node_modules", "*.tmp"],
        State = BindingState.Pulling,
        Rights = GrantRights.Write,
        Base = new ObjectHash(Id(0x31)),
        Head = new ObjectHash(Id(0x32)),
        Leaves = 2,
        Files = 10_000,
        Bytes = 67_108_864,
        LocalChanges = 1,
        Conflicts = 3,
        LastRun = 1_800_000_000_000,
        Runs = 42,
        Pending = true,
        Error = "",
    };

    private static readonly Conflict Conflict = new()
    {
        Id = 7,
        Directory = "/home/alice/shared",
        Path = "docs/report.txt",
        Kind = ConflictKind.CopyWritten,
        CopyPath = "docs/report.conflict-d3d0d5da.txt",
        OtherDevice = new DeviceId(Id(0xd3)),
        Version = new ObjectHash(Id(0x33)),
        At = 1_800_000_000_500,
    };

    /// <summary>Every record type of the Client library, by name.</summary>
    public static IReadOnlyList<Type> Types() =>
        [.. typeof(SyncCall).Assembly.GetExportedTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IWireRecord<>)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)];

    /// <summary>The sample of a record type.</summary>
    public static object Of(Type type) => type.Name switch
    {
        nameof(Sync.Binding) => Binding,
        nameof(Sync.Conflict) => Conflict,
        nameof(BindingsRequest) => new BindingsRequest(),
        nameof(BindingsReply) => new BindingsReply { Bindings = [Binding, Binding with { Directory = "/srv/music", State = BindingState.Error, Error = "the directory is gone", Ignore = [] }] },
        nameof(BindRequest) => new BindRequest { Directory = "/home/alice/shared", PackageId = new PackageId(Id(0xa7)), Label = "main", Symlinks = SymlinkPolicy.Ignore, Ignore = ["*.bak"] },
        nameof(BindReply) => new BindReply { Binding = Binding },
        nameof(UnbindRequest) => new UnbindRequest { Directory = "/home/alice/shared" },
        nameof(UnbindReply) => new UnbindReply { Binding = Binding with { State = BindingState.InSync, Pending = false } },
        nameof(StatusRequest) => new StatusRequest { Directory = "/home/alice/shared/docs" },
        nameof(StatusReply) => new StatusReply { Bindings = [Binding] },
        nameof(ScanRequest) => new ScanRequest { Directory = "" },
        nameof(ScanReply) => new ScanReply { Queued = 2 },
        nameof(ConflictsRequest) => new ConflictsRequest { Directory = "/home/alice/shared", After = 6, Limit = 100 },
        nameof(ConflictsReply) => new ConflictsReply { Conflicts = [Conflict, Conflict with { Id = 8, Kind = ConflictKind.NameRefused, CopyPath = "", OtherDevice = default, Path = "a:b" }], More = true },
        nameof(BindingEvent) => new BindingEvent { Binding = Binding },
        nameof(ConflictEvent) => new ConflictEvent { Conflict = Conflict },
        _ => throw new ArgumentException("No sample of " + type.Name, nameof(type)),
    };

    /// <summary>A record's encoding, through <see cref="SyncCodec.Encode{T}"/>.</summary>
    public static byte[] Encode(object record) =>
        (byte[])typeof(SyncCodec).GetMethod(nameof(SyncCodec.Encode))!.MakeGenericMethod(record.GetType()).Invoke(null, [record])!;

    /// <summary>Reads a payload as a record type, through <see cref="SyncCodec.TryDecode{T}"/>.</summary>
    public static bool TryDecode(Type type, byte[] payload, out object? record)
    {
        object?[] arguments = [new ReadOnlyMemory<byte>(payload), null];
        bool read = (bool)typeof(SyncCodec).GetMethod(nameof(SyncCodec.TryDecode))!.MakeGenericMethod(type).Invoke(null, arguments)!;
        record = arguments[1];
        return read;
    }

    /// <summary>Whether a prefix of a payload reads as the type, its reader at the prefix's end (it must not).</summary>
    public static bool PrefixReads(Type type, byte[] prefix) =>
        (bool)typeof(SyncSamples).GetMethod(nameof(PrefixReadsOf), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, [prefix])!;

    public static Hash32 Id(byte first) => new([first, .. Enumerable.Range(1, 31).Select(i => (byte)(first ^ (i * 3)))]);

    private static bool PrefixReadsOf<T>(byte[] prefix)
        where T : class, IWireRecord<T>
    {
        var reader = new WireReader(prefix);
        return T.TryRead(ref reader, out _) && reader.Ok && reader.IsAtEnd;
    }
}
