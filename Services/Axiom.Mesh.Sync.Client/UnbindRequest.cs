using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>
/// <c>unbind</c> (SYNC-SERVICE.md §5.3): ends the binding of a directory: its run is cancelled, its watcher, rows and marker
/// file removed; the files stay.
/// </summary>
public sealed record UnbindRequest : ISyncMethod<UnbindRequest, UnbindReply>
{
    /// <inheritdoc />
    public static string Method => SyncMethods.Unbind;

    /// <summary>The bound directory, absolute.</summary>
    public string Directory { get; init; } = "";

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out UnbindRequest? value)
    {
        var read = new UnbindRequest { Directory = Wire.ReadString(ref reader, SyncLimits.PathLength) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => Wire.WriteString(ref writer, Directory, SyncLimits.PathLength);
}
