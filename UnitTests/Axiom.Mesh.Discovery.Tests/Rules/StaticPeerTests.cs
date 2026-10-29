using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace Axiom.Mesh.Discovery.Tests.Rules;

/// <summary>SPEC.md §3.3: addresses the user configured, dialled on start and on a schedule; the handshake decides.</summary>
public sealed class StaticPeerTests
{
    // §3.3: a static address claims no device; the TLS handshake decides whether the node there is acceptable.
    [Fact]
    public async Task AStaticPeerAnnouncementHasAllZeroIds()
    {
        var address = new IPEndPoint(IPAddress.Parse("192.168.1.7"), 41810);
        var peers = new StaticPeers(new StaticPeersOptions
        {
            Peers = [address],
            Time = new FakeTimeProvider(),
            Facts = new DiscoveryFacts(8),
            Interval = Timeout.InfiniteTimeSpan,
        });

        var heard = new List<Announcement>();
        await foreach (Announcement announcement in peers.ObserveAsync(CancellationToken.None))
            heard.Add(announcement);

        Announcement only = Assert.Single(heard);
        Assert.True(only.Device.Value.IsZero);
        Assert.True(only.User.Value.IsZero);
        Assert.Equal(address, only.Address);
        Assert.Equal(DiscoverySource.Static, only.Source);
    }

    // §3.3: "dial on start and on a schedule": the list is offered again after every interval of the node's clock.
    [Fact]
    public async Task TheListIsOfferedOnStartAndAgainAfterEveryInterval()
    {
        var time = new FakeTimeProvider();
        var peers = new StaticPeers(new StaticPeersOptions
        {
            Peers = [new IPEndPoint(IPAddress.Parse("192.168.1.7"), 41810)],
            Time = time,
            Facts = new DiscoveryFacts(8),
            Interval = TimeSpan.FromMinutes(1),
        });

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<Announcement> stream = peers.ObserveAsync(stop.Token).GetAsyncEnumerator(stop.Token);

        Assert.True(await stream.MoveNextAsync());
        ValueTask<bool> second = stream.MoveNextAsync();
        Assert.False(second.IsCompleted);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await second);
    }

    // §3.3: an address the user typed must name a port that can be dialled.
    [Fact]
    public void AStaticPeerOnPortZeroIsRefusedWhenConfigured()
    {
        Assert.Throws<ArgumentException>(() => new StaticPeers(new StaticPeersOptions
        {
            Peers = [new IPEndPoint(IPAddress.Loopback, 0)],
            Time = new FakeTimeProvider(),
            Facts = new DiscoveryFacts(8),
        }));
    }
}
