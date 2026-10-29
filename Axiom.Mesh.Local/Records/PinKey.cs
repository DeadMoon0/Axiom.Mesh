using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The key of a pin, the cursor of <c>ListPins</c> (LOCAL-SPEC.md §5.11).</summary>
public sealed record PinKey : IWireRecord<PinKey>
{
    /// <summary>The pinned object.</summary>
    public ObjectHash Hash { get; init; }

    /// <summary>Why it is pinned.</summary>
    public PinReason Reason { get; init; }

    /// <summary>The pin's source (§5.11).</summary>
    public Hash32 Source { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out PinKey? value)
    {
        var read = new PinKey
        {
            Hash = ObjectHash.Read(ref reader),
            Reason = (PinReason)reader.UInt8(),
            Source = Hash32.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Hash.Write(ref writer);
        writer.UInt8((byte)Reason);
        Source.Write(ref writer);
    }
}
