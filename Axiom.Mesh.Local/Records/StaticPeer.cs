using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A configured static peer address (LOCAL-SPEC.md §5.9).</summary>
public sealed record StaticPeer : IWireRecord<StaticPeer>
{
    /// <summary>The address, <c>host:port</c>, as configured.</summary>
    public string Address { get; init; } = "";

    /// <summary>When it was last dialled; 0 when never.</summary>
    public ulong LastDialled { get; init; }

    /// <summary>How the last dial ended.</summary>
    public DialResult LastResult { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out StaticPeer? value)
    {
        var read = new StaticPeer
        {
            Address = Wire.ReadString(ref reader, FieldBounds.AddressLength),
            LastDialled = reader.UInt64BigEndian(),
            LastResult = (DialResult)reader.UInt8(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Address, FieldBounds.AddressLength);
        writer.UInt64BigEndian(LastDialled);
        writer.UInt8((byte)LastResult);
    }
}
