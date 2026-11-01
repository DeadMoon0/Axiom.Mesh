using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary><c>binding</c> (SYNC-SERVICE.md §6.1): a binding changed state or ended a run.</summary>
public sealed record BindingEvent : SyncEvent, IWireRecord<BindingEvent>
{
    /// <inheritdoc />
    public override string Name => SyncEvents.Binding;

    /// <summary>The binding after the change.</summary>
    public Binding Binding { get; init; } = new();

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out BindingEvent? value)
    {
        var read = new BindingEvent { Binding = SyncWire.ReadRecord<Binding>(ref reader) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => Binding.Write(ref writer);
}
