using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary><c>status</c> (SYNC-SERVICE.md §5.4): the binding a path lies in, or every binding.</summary>
public sealed record StatusRequest : ISyncMethod<StatusRequest, StatusReply>
{
    /// <inheritdoc />
    public static string Method => SyncMethods.Status;

    /// <summary>A path inside a bound directory, absolute; empty for every binding.</summary>
    public string Directory { get; init; } = "";

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out StatusRequest? value)
    {
        var read = new StatusRequest { Directory = Wire.ReadString(ref reader, SyncLimits.PathLength) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => Wire.WriteString(ref writer, Directory, SyncLimits.PathLength);
}
