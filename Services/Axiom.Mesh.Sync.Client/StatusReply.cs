using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>The reply to <c>status</c> (SYNC-SERVICE.md §5.4): the one binding a path lies in, or every binding by directory.</summary>
public sealed record StatusReply : IWireRecord<StatusReply>
{
    /// <summary>The bindings, at most <see cref="SyncLimits.Bindings"/>.</summary>
    public IReadOnlyList<Binding> Bindings { get; init; } = [];

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out StatusReply? value)
    {
        var read = new StatusReply { Bindings = SyncWire.ReadList<Binding>(ref reader, SyncLimits.Bindings) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => SyncWire.WriteList(ref writer, Bindings, SyncLimits.Bindings);
}
