using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Axiom.Mesh.Discovery;

/// <summary>Lists the interfaces discovery may announce on. Dishonest: it asks the operating system, and records a fact when it cannot.</summary>
internal static class LocalInterfaces
{
    /// <summary>The interfaces that are up, not loopback, and have an address; empty (with a fact) when they cannot be read.</summary>
    /// <param name="mechanism">The shell asking, for the fact.</param>
    /// <param name="time">The shell's clock, for the fact.</param>
    /// <param name="facts">Where a failure is recorded.</param>
    /// <returns>The interfaces.</returns>
    public static IReadOnlyList<LocalInterface> Read(DiscoverySource mechanism, TimeProvider time, DiscoveryFacts facts)
    {
        NetworkInterface[] all;
        try
        {
            all = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            facts.Record(new DiscoveryFact { At = time.GetUtcNow(), Mechanism = mechanism, Kind = DiscoveryFactKind.InterfacesUnreadable, Subject = "interfaces", Detail = e.Message });
            return [];
        }

        var found = new List<LocalInterface>();
        foreach (NetworkInterface candidate in all)
        {
            try
            {
                if (candidate.OperationalStatus != OperationalStatus.Up || candidate.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                if (Describe(candidate) is { } usable)
                    found.Add(usable);
            }
            catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException)
            {
                facts.Record(new DiscoveryFact { At = time.GetUtcNow(), Mechanism = mechanism, Kind = DiscoveryFactKind.InterfaceSkipped, Subject = candidate.Name, Detail = e.Message });
            }
        }

        return found;
    }

    private static LocalInterface? Describe(NetworkInterface candidate)
    {
        IPInterfaceProperties properties = candidate.GetIPProperties();
        var v4 = new List<(IPAddress, IPAddress)>();
        var v6 = new List<IPAddress>();
        foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
        {
            if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask is { } mask)
                v4.Add((unicast.Address, mask));
            else if (unicast.Address.AddressFamily == AddressFamily.InterNetworkV6)
                v6.Add(unicast.Address);
        }

        int index = v4.Count > 0 && candidate.Supports(NetworkInterfaceComponent.IPv4) ? properties.GetIPv4Properties().Index
            : v6.Count > 0 && candidate.Supports(NetworkInterfaceComponent.IPv6) ? properties.GetIPv6Properties().Index
            : -1;
        return index < 0 ? null : new LocalInterface { Name = candidate.Name, Index = index, IPv4 = v4, IPv6 = v6 };
    }
}
