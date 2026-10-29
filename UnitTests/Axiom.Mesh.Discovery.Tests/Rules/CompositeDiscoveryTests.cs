using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Discovery.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Axiom.Mesh.Discovery.Tests.Rules;

/// <summary>DESIGN.md §6.1: mDNS, broadcast and static peers answer one question together, each claim once.</summary>
public sealed class CompositeDiscoveryTests
{
    private static Announcement Claim(byte device, string address, DiscoverySource source) =>
        new(Ids.Device(device), Ids.User(0x22), new IPEndPoint(IPAddress.Parse(address), 41810), source);

    [Fact]
    public async Task ACompositeMergesWithoutDuplicates()
    {
        var mdns = new ListedDiscovery(Claim(1, "10.0.0.1", DiscoverySource.Mdns), Claim(2, "10.0.0.2", DiscoverySource.Mdns), Claim(1, "10.0.0.1", DiscoverySource.Mdns));
        var broadcast = new ListedDiscovery(Claim(1, "10.0.0.1", DiscoverySource.Broadcast), Claim(3, "10.0.0.3", DiscoverySource.Broadcast));
        var composite = new CompositeDiscovery(new CompositeDiscoveryOptions
        {
            Parts = [mdns, broadcast],
            Time = new FakeTimeProvider(),
            Cache = new AnnouncementCache(capacity: 16, repeatAfter: TimeSpan.FromMinutes(1)),
        });

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var heard = new List<Announcement>();
        await foreach (Announcement announcement in composite.ObserveAsync(deadline.Token))
            heard.Add(announcement);

        Assert.Equal(new[] { Ids.Device(1), Ids.Device(2), Ids.Device(3) },heard.Select(a => a.Device).Order());
        Assert.True(composite.Cache.TryGet(Ids.Device(1), out Announcement? merged));
        Assert.Equal(DiscoverySource.Mdns | DiscoverySource.Broadcast, merged.Source);
    }

    [Fact]
    public async Task AnnouncingAsksEveryPartToAnnounce()
    {
        var first = new ListedDiscovery();
        var second = new ListedDiscovery();
        var composite = new CompositeDiscovery(new CompositeDiscoveryOptions
        {
            Parts = [first, second],
            Time = new FakeTimeProvider(),
            Cache = new AnnouncementCache(capacity: 16, repeatAfter: TimeSpan.FromMinutes(1)),
        });

        await composite.AnnounceAsync(CancellationToken.None);

        Assert.Equal(1, first.Announced);
        Assert.Equal(1, second.Announced);
    }
}
