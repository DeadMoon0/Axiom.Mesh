using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Discovery.Tests.Support;

namespace Axiom.Mesh.Discovery.Tests.Regression;

/// <summary>The broadcast shell on a real socket: the one network test CI can run (multicast and broadcast are filtered there).</summary>
public sealed class LoopbackSmokeTests
{
    // Damage: a shell that binds, receives and parses but never answers (a lost packet-information option, a gate keyed
    // wrongly, an answer sent to the wrong endpoint) passes every pure test and finds no node on a real LAN.
    [Fact]
    public async Task AQueryToABroadcastDiscoveryOnLoopbackIsAnsweredWithItsAnnouncement_AndNamesItsSender()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        SelfRecord node = Ids.Self(0x11, 0x22, 41810);
        SelfRecord asker = Ids.Self(0x33, 0x44, 41999);
        var facts = new DiscoveryFacts(16);
        await using BroadcastDiscovery discovery = BroadcastDiscovery.Open(new BroadcastDiscoveryOptions
        {
            Self = node,
            Time = TimeProvider.System,
            Facts = facts,
            BindAddress = IPAddress.Loopback,
            Port = 0,
            AnnounceInterval = Timeout.InfiniteTimeSpan,
        });
        IPEndPoint listening = Assert.Single(discovery.LocalEndPoints);
        await using var observed = discovery.ObserveAsync(deadline.Token).GetAsyncEnumerator(deadline.Token);
        ValueTask<bool> heard = observed.MoveNextAsync();

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        await client.SendToAsync(BroadcastCodec.Write(BroadcastKind.Query, asker), SocketFlags.None, listening, deadline.Token);

        byte[] buffer = new byte[BroadcastCodec.Length + 1];
        SocketReceiveFromResult answer = await client.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), deadline.Token);

        Assert.True(BroadcastCodec.TryParse(buffer.AsSpan(0, answer.ReceivedBytes), out BroadcastDatagram datagram), "The answer is not a well-formed datagram.");
        Assert.Equal(new BroadcastDatagram(BroadcastKind.Announcement, node.Device, node.User, node.Port), datagram);
        Assert.Equal(listening, answer.RemoteEndPoint);

        Assert.True(await heard);
        Assert.Equal(new Announcement(asker.Device, asker.User, new IPEndPoint(IPAddress.Loopback, asker.Port), DiscoverySource.Broadcast), observed.Current);
        Assert.False(facts.Reader.TryRead(out DiscoveryFact? fact), $"Nothing should have failed: {fact}");
    }
}
