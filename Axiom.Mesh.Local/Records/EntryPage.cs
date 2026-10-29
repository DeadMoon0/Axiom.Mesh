using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A page of <c>ListHistory</c> or <c>ListStored</c> (LOCAL-SPEC.md §6.4).</summary>
public sealed record EntryPage : IWireRecord<EntryPage>
{
    /// <summary>The Entries, in the requested order.</summary>
    public IReadOnlyList<Entry> Entries { get; init; } = [];

    /// <summary>Whether the page was cut; continue after its last Entry.</summary>
    public bool More { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out EntryPage? value)
    {
        var read = new EntryPage
        {
            Entries = LocalWire.ReadList<Entry>(ref reader, FieldBounds.EntriesCount),
            More = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        LocalWire.WriteList(ref writer, Entries, FieldBounds.EntriesCount);
        writer.Boolean(More);
    }
}
