using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A pin on an object (LOCAL-SPEC.md §5.11); pins order by <c>(hash, reason, source)</c>.</summary>
public sealed record Pin : IWireRecord<Pin>
{
    /// <summary>The pinned object.</summary>
    public ObjectHash Hash { get; init; }

    /// <summary>Why it is pinned (§14.8).</summary>
    public PinReason Reason { get; init; }

    /// <summary>The Package id for Head, Policy and Outbound (the user id for a Roster's Head pin); all zero for User; H(service name) for Service.</summary>
    public Hash32 Source { get; init; }

    /// <summary>When the pin was made.</summary>
    public ulong Since { get; init; }

    /// <summary>The object's size; 0 when it is not stored.</summary>
    public ulong Size { get; init; }

    /// <summary>The pin's key, the cursor of <c>ListPins</c>.</summary>
    public PinKey Key => new() { Hash = Hash, Reason = Reason, Source = Source };

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Pin? value)
    {
        var read = new Pin
        {
            Hash = ObjectHash.Read(ref reader),
            Reason = (PinReason)reader.UInt8(),
            Source = Hash32.Read(ref reader),
            Since = reader.UInt64BigEndian(),
            Size = reader.UInt64BigEndian(),
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
        writer.UInt64BigEndian(Since);
        writer.UInt64BigEndian(Size);
    }
}
