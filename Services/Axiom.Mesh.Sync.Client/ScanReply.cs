using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>The reply to <c>scan</c> (SYNC-SERVICE.md §5.5): how many bindings got a run queued.</summary>
public sealed record ScanReply : IWireRecord<ScanReply>
{
    /// <summary>The bindings whose run was queued.</summary>
    public ushort Queued { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ScanReply? value)
    {
        var read = new ScanReply { Queued = reader.UInt16BigEndian() };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => writer.UInt16BigEndian(Queued);
}
