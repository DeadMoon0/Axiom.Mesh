using System.Net;

namespace Axiom.Mesh.Discovery.Tests.Regression;

/// <summary>Where the broadcast shell sends on an IPv4 interface: the subnet's directed broadcast address.</summary>
public sealed class DirectedBroadcastTests
{
    // Damage: a wrong mask computation would broadcast into another subnet (or to a host) and reach no node.
    [Theory]
    // A /24 home network.
    [InlineData("192.168.1.20", "255.255.255.0", "192.168.1.255")]
    // A /16.
    [InlineData("10.4.7.9", "255.255.0.0", "10.4.255.255")]
    // A /30 point-to-point link still has a broadcast address.
    [InlineData("172.16.0.1", "255.255.255.252", "172.16.0.3")]
    // A /31 has none.
    [InlineData("172.16.0.1", "255.255.255.254", null)]
    // A /32 has none.
    [InlineData("172.16.0.1", "255.255.255.255", null)]
    public void TheDirectedBroadcastSetsEveryHostBit(string address, string mask, string? expected)
    {
        IPAddress? broadcast = LocalInterface.DirectedBroadcast(IPAddress.Parse(address), IPAddress.Parse(mask));

        Assert.Equal(expected, broadcast?.ToString());
    }
}
