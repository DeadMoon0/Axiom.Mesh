using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>An object travelling between this node and a peer (LOCAL-SPEC.md §5.16).</summary>
public sealed record Transfer : IWireRecord<Transfer>
{
    /// <summary>The peer device.</summary>
    public DeviceId Peer { get; init; }

    /// <summary>The Package it was wanted through; all zero for a Roster.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>The object.</summary>
    public ObjectHash Hash { get; init; }

    /// <summary>In or out.</summary>
    public TransferDirection Direction { get; init; }

    /// <summary>The object's size; 0 when unknown.</summary>
    public ulong Size { get; init; }

    /// <summary>Where it stands.</summary>
    public TransferState State { get; init; }

    /// <summary>When it got there.</summary>
    public ulong At { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Transfer? value)
    {
        var read = new Transfer
        {
            Peer = DeviceId.Read(ref reader),
            PackageId = PackageId.Read(ref reader),
            Hash = ObjectHash.Read(ref reader),
            Direction = (TransferDirection)reader.UInt8(),
            Size = reader.UInt64BigEndian(),
            State = (TransferState)reader.UInt8(),
            At = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Peer.Write(ref writer);
        PackageId.Write(ref writer);
        Hash.Write(ref writer);
        writer.UInt8((byte)Direction);
        writer.UInt64BigEndian(Size);
        writer.UInt8((byte)State);
        writer.UInt64BigEndian(At);
    }
}
