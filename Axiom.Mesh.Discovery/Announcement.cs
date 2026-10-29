using System.Net;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// One claim heard on the network: a node at <paramref name="Address"/> says it is <paramref name="Device"/> of
/// <paramref name="User"/> (SPEC.md §3). A hint, never trusted: only the TLS handshake (SPEC.md §4.3) proves who answers.
/// </summary>
/// <remarks>
/// A node MUST NOT dial an address whose claimed device is not, in a roster it holds, a device of a known user
/// (SPEC.md §3). The one exception is an announcement of <see cref="DiscoverySource.Static"/>: the user typed the
/// address, the ids are all zero, and the handshake decides (SPEC.md §3.3). The codecs never produce a zero id, so a
/// datagram cannot pose as a static peer; still, decide on <see cref="Source"/>, never on the ids.
/// </remarks>
/// <param name="Device">The device id the node claims; all zero for a static peer.</param>
/// <param name="User">The user id the node claims; all zero for a static peer.</param>
/// <param name="Address">Where the node listens: the sender's address with the mesh port it announced.</param>
/// <param name="Source">The mechanism that reported it.</param>
[ClosedShape("SPEC.md §3 states the whole claim: two ids and an address; the source says how it was heard.")]
public sealed record Announcement(DeviceId Device, UserId User, IPEndPoint Address, DiscoverySource Source)
{
    /// <summary>The announcement of a static peer (SPEC.md §3.3): the address the user configured, with all-zero ids.</summary>
    /// <param name="address">The address the user configured.</param>
    /// <returns>An announcement of <see cref="DiscoverySource.Static"/>.</returns>
    public static Announcement ForStaticPeer(IPEndPoint address) => new(default, default, address, DiscoverySource.Static);

    /// <summary>Whether two announcements make the same claim, whichever mechanism reported them.</summary>
    /// <param name="other">The other announcement.</param>
    /// <returns><see langword="true"/> when device, user and address are equal.</returns>
    public bool ClaimsTheSameAs(Announcement other) =>
        Device == other.Device && User == other.User && Address.Equals(other.Address);
}
