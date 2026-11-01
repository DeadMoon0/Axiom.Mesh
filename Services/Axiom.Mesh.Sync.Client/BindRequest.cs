using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>
/// <c>bind</c> (SYNC-SERVICE.md §5.2): binds a directory to a followed Archive and queues its first run. The reply comes once
/// the binding is recorded and the marker file written; the run reports itself with <c>binding</c> events.
/// </summary>
public sealed record BindRequest : ISyncMethod<BindRequest, BindReply>
{
    /// <inheritdoc />
    public static string Method => SyncMethods.Bind;

    /// <summary>The directory, absolute; it must exist.</summary>
    public string Directory { get; init; } = "";

    /// <summary>The Archive, followed on this Host.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>The branch to follow and publish to; <c>main</c> by default.</summary>
    public string Label { get; init; } = SyncProtocol.DefaultLabel;

    /// <summary>What to do with a symlink of the branch.</summary>
    public SymlinkPolicy Symlinks { get; init; } = SymlinkPolicy.AsFile;

    /// <summary>Names to leave alone: exact names and <c>*.ext</c>, at most <see cref="SyncLimits.IgnorePatterns"/>.</summary>
    public IReadOnlyList<string> Ignore { get; init; } = [];

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out BindRequest? value)
    {
        var read = new BindRequest
        {
            Directory = Wire.ReadString(ref reader, SyncLimits.PathLength),
            PackageId = PackageId.Read(ref reader),
            Label = Wire.ReadString(ref reader, SyncLimits.LabelLength),
            Symlinks = (SymlinkPolicy)reader.UInt8(),
            Ignore = SyncWire.ReadStrings(ref reader, SyncLimits.IgnorePatterns, SyncLimits.PatternLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Directory, SyncLimits.PathLength);
        PackageId.Write(ref writer);
        Wire.WriteString(ref writer, Label, SyncLimits.LabelLength);
        writer.UInt8((byte)Symlinks);
        SyncWire.WriteStrings(ref writer, Ignore, SyncLimits.IgnorePatterns, SyncLimits.PatternLength);
    }
}
