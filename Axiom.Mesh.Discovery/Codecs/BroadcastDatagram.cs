using System.Net;

namespace Axiom.Mesh.Discovery;

/// <summary>A broadcast datagram as read (SPEC.md §3.2). Both kinds carry the sender's own ids and mesh port.</summary>
/// <param name="Kind">Query or announcement.</param>
/// <param name="Device">The device id the sender claims; never zero once parsed.</param>
/// <param name="User">The user id the sender claims; never zero once parsed.</param>
/// <param name="Port">The TCP port of the sender's mesh listener; never 0 once parsed.</param>
[ClosedShape("SPEC.md §3.2 fixes the datagram at 71 bytes; a new field is a new magic.")]
public readonly record struct BroadcastDatagram(BroadcastKind Kind, DeviceId Device, UserId User, ushort Port)
{
    /// <summary>The claim this datagram makes, at the address it came from: a query names its sender as much as an announcement does.</summary>
    /// <param name="source">The datagram's source address.</param>
    /// <returns>An announcement of <see cref="DiscoverySource.Broadcast"/>.</returns>
    public Announcement ToAnnouncement(IPAddress source) => new(Device, User, new IPEndPoint(source, Port), DiscoverySource.Broadcast);
}
