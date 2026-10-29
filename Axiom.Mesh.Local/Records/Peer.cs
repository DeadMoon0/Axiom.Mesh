using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A device this Host knows of as a peer (LOCAL-SPEC.md §5.7).</summary>
public sealed record Peer : IWireRecord<Peer>
{
    /// <summary>The device, as claimed by discovery or proven by a session.</summary>
    public DeviceId DeviceId { get; init; }

    /// <summary>Its user, as claimed or proven; all zero when unknown.</summary>
    public UserId UserId { get; init; }

    /// <summary>Whether the device is in a Roster this Host holds and not removed.</summary>
    public bool Known { get; init; }

    /// <summary>Its addresses, <c>host:port</c>, the most recent 8, ascending as byte strings.</summary>
    public IReadOnlyList<string> Addresses { get; init; } = [];

    /// <summary>Where this node learned of it.</summary>
    public PeerSources Sources { get; init; }

    /// <summary>When it was first seen.</summary>
    public ulong FirstSeen { get; init; }

    /// <summary>When it was last seen.</summary>
    public ulong LastSeen { get; init; }

    /// <summary>Its state.</summary>
    public PeerState State { get; init; }

    /// <summary>Until when it is not dialled; 0 when not backing off.</summary>
    public ulong BackoffUntil { get; init; }

    /// <summary>Violations recorded against it in the last hour.</summary>
    public uint Violations { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Peer? value)
    {
        var read = new Peer
        {
            DeviceId = DeviceId.Read(ref reader),
            UserId = UserId.Read(ref reader),
            Known = reader.Boolean(),
            Addresses = LocalWire.ReadStrings(ref reader, FieldBounds.AddressLength, FieldBounds.AddressesCount),
            Sources = (PeerSources)reader.UInt8(),
            FirstSeen = reader.UInt64BigEndian(),
            LastSeen = reader.UInt64BigEndian(),
            State = (PeerState)reader.UInt8(),
            BackoffUntil = reader.UInt64BigEndian(),
            Violations = reader.UInt32BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        DeviceId.Write(ref writer);
        UserId.Write(ref writer);
        writer.Boolean(Known);
        LocalWire.WriteStrings(ref writer, Addresses, FieldBounds.AddressLength, FieldBounds.AddressesCount);
        writer.UInt8((byte)Sources);
        writer.UInt64BigEndian(FirstSeen);
        writer.UInt64BigEndian(LastSeen);
        writer.UInt8((byte)State);
        writer.UInt64BigEndian(BackoffUntil);
        writer.UInt32BigEndian(Violations);
    }
}
