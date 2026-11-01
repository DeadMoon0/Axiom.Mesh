using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>
/// A conflict the sync service recorded (SYNC-SERVICE.md §4, §7): a copy written beside a path two leaves changed
/// differently, a name not written, a file not published. There is no method to resolve one: the user deletes the copy
/// they do not want, and the next run publishes that.
/// </summary>
public sealed record Conflict : IWireRecord<Conflict>
{
    /// <summary>The conflict's number, ascending in the order the service recorded them; <c>conflicts</c> pages after it.</summary>
    public ulong Id { get; init; }

    /// <summary>The bound directory.</summary>
    public string Directory { get; init; } = "";

    /// <summary>The path it concerns, relative to the directory with <c>/</c>.</summary>
    public string Path { get; init; } = "";

    /// <summary>Why it was recorded.</summary>
    public ConflictKind Kind { get; init; }

    /// <summary>For <see cref="ConflictKind.CopyWritten"/>: the path the copy of the other side was written to; empty otherwise.</summary>
    public string CopyPath { get; init; } = "";

    /// <summary>For <see cref="ConflictKind.CopyWritten"/>: the device whose leaf lost; zero otherwise.</summary>
    public DeviceId OtherDevice { get; init; }

    /// <summary>The Version the conflict belongs to: the merge or the leaf written; zero when it concerns the directory only.</summary>
    public ObjectHash Version { get; init; }

    /// <summary>When it was recorded, in milliseconds since the Unix epoch.</summary>
    public ulong At { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Conflict? value)
    {
        var read = new Conflict
        {
            Id = reader.UInt64BigEndian(),
            Directory = Wire.ReadString(ref reader, SyncLimits.PathLength),
            Path = Wire.ReadString(ref reader, SyncLimits.PathLength),
            Kind = (ConflictKind)reader.UInt8(),
            CopyPath = Wire.ReadString(ref reader, SyncLimits.PathLength),
            OtherDevice = DeviceId.Read(ref reader),
            Version = ObjectHash.Read(ref reader),
            At = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        writer.UInt64BigEndian(Id);
        Wire.WriteString(ref writer, Directory, SyncLimits.PathLength);
        Wire.WriteString(ref writer, Path, SyncLimits.PathLength);
        writer.UInt8((byte)Kind);
        Wire.WriteString(ref writer, CopyPath, SyncLimits.PathLength);
        OtherDevice.Write(ref writer);
        Version.Write(ref writer);
        writer.UInt64BigEndian(At);
    }
}
