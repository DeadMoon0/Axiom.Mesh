using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;

namespace Axiom.Mesh.Discovery;

/// <summary>One network interface that is up and not loopback, as the shells use it: its index and its unicast addresses.</summary>
internal sealed record LocalInterface
{
    /// <summary>The operating system's name, for facts.</summary>
    public required string Name { get; init; }

    /// <summary>The operating system's index; the announcement gate's key and the IPv6 scope id.</summary>
    public required int Index { get; init; }

    /// <summary>The IPv4 unicast addresses with their subnet masks.</summary>
    public required IReadOnlyList<(IPAddress Address, IPAddress Mask)> IPv4 { get; init; }

    /// <summary>The IPv6 unicast addresses.</summary>
    public required IReadOnlyList<IPAddress> IPv6 { get; init; }

    /// <summary>
    /// The directed broadcast address of a subnet: the address with every host bit set. <see langword="null"/> for a
    /// /31 or /32, which has no broadcast address.
    /// </summary>
    /// <param name="address">An IPv4 address.</param>
    /// <param name="mask">Its subnet mask.</param>
    /// <returns>The broadcast address, or <see langword="null"/>.</returns>
    public static IPAddress? DirectedBroadcast(IPAddress address, IPAddress mask)
    {
        uint host = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
        uint bits = BinaryPrimitives.ReadUInt32BigEndian(mask.GetAddressBytes());
        if (~bits <= 1)
            return null;

        byte[] broadcast = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(broadcast, host | ~bits);
        return new IPAddress(broadcast);
    }
}
