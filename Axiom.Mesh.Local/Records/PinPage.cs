using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A page of <c>ListPins</c> (LOCAL-SPEC.md §6.5).</summary>
public sealed record PinPage : IWireRecord<PinPage>
{
    /// <summary>The pins, ascending by their key.</summary>
    public IReadOnlyList<Pin> Pins { get; init; } = [];

    /// <summary>Whether the page was cut; continue after its last pin.</summary>
    public bool More { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out PinPage? value)
    {
        var read = new PinPage
        {
            Pins = LocalWire.ReadList<Pin>(ref reader, FieldBounds.PinsCount),
            More = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        LocalWire.WriteList(ref writer, Pins, FieldBounds.PinsCount);
        writer.Boolean(More);
    }
}
