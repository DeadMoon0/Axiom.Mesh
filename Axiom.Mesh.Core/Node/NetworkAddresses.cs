using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Axiom.Mesh.Core;

/// <summary>
/// The addresses this device has on its networks, read from the operating system: what a pairing code lists (PAIR-SPEC.md
/// §2.2) when nothing else is named. The one place Core asks the platform for its interfaces.
/// </summary>
internal static class NetworkAddresses
{
    /// <summary>The unicast addresses of every interface that is up and not loopback, IPv4 before IPv6.</summary>
    /// <returns>The addresses; empty when the platform names none or refuses to.</returns>
    public static IReadOnlyList<IPAddress> Read()
    {
        try
        {
            return [.. NetworkInterface.GetAllNetworkInterfaces()
                .Where(i => i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(i => i.GetIPProperties().UnicastAddresses)
                .Select(u => u.Address)
                .Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 && !IPAddress.IsLoopback(a))
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .Distinct()];
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }
}
