using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary><c>conflicts</c> (SYNC-SERVICE.md §5.6): a page of the conflicts recorded, oldest first.</summary>
public sealed record ConflictsRequest : ISyncMethod<ConflictsRequest, ConflictsReply>
{
    /// <inheritdoc />
    public static string Method => SyncMethods.Conflicts;

    /// <summary>A path inside a bound directory, absolute, for that binding only; empty for every binding.</summary>
    public string Directory { get; init; } = "";

    /// <summary>Only conflicts with a greater <see cref="Conflict.Id"/>; 0 from the first.</summary>
    public ulong After { get; init; }

    /// <summary>The most conflicts to return, 1 to <see cref="SyncLimits.ConflictsPage"/>.</summary>
    public ushort Limit { get; init; } = SyncLimits.ConflictsPage;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ConflictsRequest? value)
    {
        var read = new ConflictsRequest
        {
            Directory = Wire.ReadString(ref reader, SyncLimits.PathLength),
            After = reader.UInt64BigEndian(),
            Limit = reader.UInt16BigEndian(),
        };
        if (reader.Ok && read.Limit is 0 or > SyncLimits.ConflictsPage)
            reader.Fail();
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        if (Limit is 0 or > SyncLimits.ConflictsPage)
            throw new ArgumentException($"A conflicts page holds 1 to {SyncLimits.ConflictsPage} conflicts, not {Limit}.", nameof(Limit));
        Wire.WriteString(ref writer, Directory, SyncLimits.PathLength);
        writer.UInt64BigEndian(After);
        writer.UInt16BigEndian(Limit);
    }
}
