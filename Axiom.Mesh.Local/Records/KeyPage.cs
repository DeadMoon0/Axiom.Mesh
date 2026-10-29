using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A page of <c>ListKeys</c> (LOCAL-SPEC.md §10.3).</summary>
public sealed record KeyPage : IWireRecord<KeyPage>
{
    /// <summary>The keys with any point or total, ascending as byte strings.</summary>
    public IReadOnlyList<string> Keys { get; init; } = [];

    /// <summary>Whether the page was cut; continue after its last key.</summary>
    public bool More { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out KeyPage? value)
    {
        var read = new KeyPage
        {
            Keys = LocalWire.ReadStrings(ref reader, FieldBounds.TelemetryKeyLength, FieldBounds.KeysCount),
            More = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        LocalWire.WriteStrings(ref writer, Keys, FieldBounds.TelemetryKeyLength, FieldBounds.KeysCount);
        writer.Boolean(More);
    }
}
