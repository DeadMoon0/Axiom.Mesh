using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary><c>conflict</c> (SYNC-SERVICE.md §6.2): the service recorded a conflict.</summary>
public sealed record ConflictEvent : SyncEvent, IWireRecord<ConflictEvent>
{
    /// <inheritdoc />
    public override string Name => SyncEvents.Conflict;

    /// <summary>The conflict.</summary>
    public Conflict Conflict { get; init; } = new();

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ConflictEvent? value)
    {
        var read = new ConflictEvent { Conflict = SyncWire.ReadRecord<Conflict>(ref reader) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => Conflict.Write(ref writer);
}
