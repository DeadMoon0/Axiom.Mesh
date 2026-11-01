using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>
/// <c>scan</c> (SYNC-SERVICE.md §5.5): queues a run of the binding a path lies in, or of every binding, without waiting for
/// the watcher.
/// </summary>
public sealed record ScanRequest : ISyncMethod<ScanRequest, ScanReply>
{
    /// <inheritdoc />
    public static string Method => SyncMethods.Scan;

    /// <summary>A path inside a bound directory, absolute; empty for every binding.</summary>
    public string Directory { get; init; } = "";

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ScanRequest? value)
    {
        var read = new ScanRequest { Directory = Wire.ReadString(ref reader, SyncLimits.PathLength) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => Wire.WriteString(ref writer, Directory, SyncLimits.PathLength);
}
