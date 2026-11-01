using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>The reply to <c>bindings</c> (SYNC-SERVICE.md §5.1).</summary>
public sealed record BindingsReply : IWireRecord<BindingsReply>
{
    /// <summary>The bindings, by directory, at most <see cref="SyncLimits.Bindings"/>.</summary>
    public IReadOnlyList<Binding> Bindings { get; init; } = [];

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out BindingsReply? value)
    {
        var read = new BindingsReply { Bindings = SyncWire.ReadList<Binding>(ref reader, SyncLimits.Bindings) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => SyncWire.WriteList(ref writer, Bindings, SyncLimits.Bindings);
}
