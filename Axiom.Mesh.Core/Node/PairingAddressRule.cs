using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Axiom.Mesh.Core;

/// <summary>
/// Which of this device's addresses a pairing code lists, in the order the entering side tries them (PAIR-SPEC.md §2.2):
/// the addresses of interfaces that are up; routable ones before link-local ones, which are listed only when no routable
/// one exists, and loopback only when nothing else does; among the routable ones, only the interfaces with a gateway
/// when any has one (a virtual switch or a VPN adapter without a default route is not where the other PC is reached),
/// else all of them; within a tier, IPv4 before IPv6, then as the platform listed them.
/// </summary>
internal static class PairingAddressRule
{
    /// <summary>The addresses a code lists.</summary>
    /// <param name="candidates">Every unicast address of every interface, as the platform lists them.</param>
    /// <returns>The chosen addresses in order; empty when no interface is up.</returns>
    public static IReadOnlyList<IPAddress> Choose(IEnumerable<InterfaceAddress> candidates)
    {
        InterfaceAddress[] up = [.. candidates.Where(c => c.IsUp && c.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)];
        InterfaceAddress[] tier = Tier(up, Tiering.Routable) is { Length: > 0 } routable ? WithGatewayWhenAnyHasOne(routable)
            : Tier(up, Tiering.LinkLocal) is { Length: > 0 } linkLocal ? linkLocal
            : Tier(up, Tiering.Loopback);
        return [.. tier
            .Select((c, index) => (Candidate: c, Index: index))
            .OrderBy(c => c.Candidate.Address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ThenBy(c => c.Index)
            .Select(c => c.Candidate.Address)
            .Distinct()];
    }

    private static InterfaceAddress[] Tier(InterfaceAddress[] up, Tiering tier) => [.. up.Where(c => TierOf(c.Address) == tier)];

    private static InterfaceAddress[] WithGatewayWhenAnyHasOne(InterfaceAddress[] routable) =>
        routable.Any(c => c.HasGateway) ? [.. routable.Where(c => c.HasGateway)] : routable;

    private static Tiering TierOf(IPAddress address) =>
        IPAddress.IsLoopback(address) ? Tiering.Loopback
        : address.IsIPv6LinkLocal || IsIPv4LinkLocal(address) ? Tiering.LinkLocal
        : Tiering.Routable;

    // 169.254.0.0/16, RFC 3927.
    private static bool IsIPv4LinkLocal(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private enum Tiering
    {
        Routable = 0,
        LinkLocal = 1,
        Loopback = 2,
    }
}
