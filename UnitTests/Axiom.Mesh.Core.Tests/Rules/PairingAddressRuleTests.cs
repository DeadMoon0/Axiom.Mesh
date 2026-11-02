using System.Linq;
using System.Net;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// PAIR-SPEC.md §2.2 (debt 49): a pairing code lists the addresses of interfaces that are up, routable before link-local
/// before loopback (each tier only when the one before is empty), of the routable ones only those with a gateway when
/// any has one (so a virtual switch or a VPN adapter without a default route does not lengthen the code), IPv4 before IPv6.
/// </summary>
public sealed class PairingAddressRuleTests
{
    [Theory]
    // a LAN address with a gateway, a virtual switch's address without one, a link-local and a loopback address: the LAN address alone
    [InlineData("172.30.0.1|up|-;192.168.1.20|up|gw;fe80::1|up|-;127.0.0.1|up|-", "192.168.1.20")]
    // two interfaces with a gateway (a LAN and a VPN): both, as the platform listed them
    [InlineData("10.8.0.2|up|gw;192.168.1.20|up|gw", "10.8.0.2,192.168.1.20")]
    // no interface has a gateway (a LAN without a router): every routable address
    [InlineData("192.168.56.1|up|-;10.0.0.5|up|-", "192.168.56.1,10.0.0.5")]
    // an interface that is down is never listed
    [InlineData("192.168.1.20|down|gw;10.0.0.5|up|-", "10.0.0.5")]
    // IPv4 before IPv6 within a tier
    [InlineData("2001:db8::5|up|gw;192.168.1.20|up|gw", "192.168.1.20,2001:db8::5")]
    // no routable address: the link-local ones, IPv4 (169.254/16) before IPv6
    [InlineData("fe80::1|up|-;169.254.3.4|up|-;127.0.0.1|up|-", "169.254.3.4,fe80::1")]
    // nothing but loopback: loopback, so a device alone can still pair with itself over its own interface
    [InlineData("127.0.0.1|up|-;::1|up|-", "127.0.0.1,::1")]
    // nothing up: an empty list
    [InlineData("192.168.1.20|down|gw", "")]
    public void RoutableBeforeLinkLocalBeforeLoopback_OnlyGatewayInterfacesWhenAnyHasOne_IPv4BeforeIPv6(string interfaces, string expected)
    {
        InterfaceAddress[] candidates = [.. interfaces.Split(';').Select(c => c.Split('|')).Select(p => new InterfaceAddress(IPAddress.Parse(p[0]), p[1] == "up", p[2] == "gw"))];

        string chosen = string.Join(",", PairingAddressRule.Choose(candidates).Select(a => a.ToString()));

        Assert.Equal(expected, chosen);
    }
}
