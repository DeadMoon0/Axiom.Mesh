using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>The reply to <c>conflicts</c> (SYNC-SERVICE.md §5.6): a page, cut at the limit and at 1 MiB.</summary>
public sealed record ConflictsReply : IWireRecord<ConflictsReply>
{
    /// <summary>The conflicts, ascending by <see cref="Conflict.Id"/>.</summary>
    public IReadOnlyList<Conflict> Conflicts { get; init; } = [];

    /// <summary>Whether more follow; page on after the last <see cref="Conflict.Id"/>.</summary>
    public bool More { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ConflictsReply? value)
    {
        var read = new ConflictsReply
        {
            Conflicts = SyncWire.ReadList<Conflict>(ref reader, SyncLimits.ConflictsPage),
            More = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        SyncWire.WriteList(ref writer, Conflicts, SyncLimits.ConflictsPage);
        writer.Boolean(More);
    }
}
