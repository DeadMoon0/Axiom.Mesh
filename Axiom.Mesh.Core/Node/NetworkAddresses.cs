using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace Axiom.Mesh.Core;

/// <summary>
/// The addresses this device has on its networks, read from the operating system and chosen by
/// <see cref="PairingAddressRule"/>: what a pairing code lists (PAIR-SPEC.md §2.2) when nothing else is named. The one
/// place Core asks the platform for its interfaces.
/// </summary>
internal static class NetworkAddresses
{
    /// <summary>The addresses a pairing code lists, in order.</summary>
    /// <returns>The addresses; empty when the platform names none or refuses to.</returns>
    public static IReadOnlyList<IPAddress> Read()
    {
        try
        {
            return PairingAddressRule.Choose(NetworkInterface.GetAllNetworkInterfaces().SelectMany(Of));
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    private static IEnumerable<InterfaceAddress> Of(NetworkInterface face)
    {
        IPInterfaceProperties properties = face.GetIPProperties();
        bool up = face.OperationalStatus == OperationalStatus.Up;
        bool gateway = HasGateway(properties);
        return properties.UnicastAddresses.Select(u => new InterfaceAddress(u.Address, up, gateway));
    }

    // Some platforms do not say (Android among them); an interface is then ordered as one without a gateway.
    private static bool HasGateway(IPInterfaceProperties properties)
    {
        try
        {
            return properties.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}
