using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>The reply to <c>bind</c> (SYNC-SERVICE.md §5.2): the binding as recorded, its first run queued.</summary>
public sealed record BindReply : IWireRecord<BindReply>
{
    /// <summary>The binding.</summary>
    public Binding Binding { get; init; } = new();

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out BindReply? value)
    {
        var read = new BindReply { Binding = SyncWire.ReadRecord<Binding>(ref reader) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => Binding.Write(ref writer);
}
