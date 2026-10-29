using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Axiom.Mesh.Discovery.Tests.Support;

namespace Axiom.Mesh.Discovery.Tests.Rules;

/// <summary>
/// DESIGN.md §6.1 and §14: "an unknown node costs nothing but a cache entry"; discovery spam is only a hint. The newest
/// claim of a device wins.
/// </summary>
public sealed class AnnouncementCacheTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static Announcement Claim(DeviceId device, string address, DiscoverySource source) =>
        new(device, Ids.User(0x22), new IPEndPoint(IPAddress.Parse(address), 41810), source);

    [Fact]
    public void AThousandUnknownAnnouncementsCostOnlyABoundedCache()
    {
        var cache = new AnnouncementCache(capacity: 64, repeatAfter: Minute);

        for (int i = 0; i < 1_000; i++)
            Assert.Equal(AnnouncementNovelty.New, cache.Observe(Claim(Ids.Numbered(i), "10.0.0.9", DiscoverySource.Broadcast), TimeSpan.FromMilliseconds(i)));

        IReadOnlyList<Announcement> held = cache.Snapshot();
        Assert.Equal(64, cache.Count);
        Assert.Equal(Enumerable.Range(936, 64).Select(Ids.Numbered), held.Select(a => a.Device));
        Assert.False(cache.TryGet(Ids.Numbered(0), out _));
    }

    // The entry heard of least recently is evicted, not the one added first: a device that keeps announcing stays.
    [Fact]
    public void TheClaimHeardOfLeastRecentlyIsEvicted()
    {
        var cache = new AnnouncementCache(capacity: 2, repeatAfter: Minute);
        cache.Observe(Claim(Ids.Device(1), "10.0.0.1", DiscoverySource.Mdns), TimeSpan.FromSeconds(1));
        cache.Observe(Claim(Ids.Device(2), "10.0.0.2", DiscoverySource.Mdns), TimeSpan.FromSeconds(2));
        cache.Observe(Claim(Ids.Device(1), "10.0.0.1", DiscoverySource.Mdns), TimeSpan.FromSeconds(3));

        cache.Observe(Claim(Ids.Device(3), "10.0.0.3", DiscoverySource.Mdns), TimeSpan.FromSeconds(4));

        Assert.True(cache.TryGet(Ids.Device(1), out _));
        Assert.False(cache.TryGet(Ids.Device(2), out _));
        Assert.True(cache.TryGet(Ids.Device(3), out _));
    }

    [Fact]
    public void TheNewestClaimOfADeviceWins()
    {
        var cache = new AnnouncementCache(capacity: 8, repeatAfter: Minute);
        Assert.Equal(AnnouncementNovelty.New, cache.Observe(Claim(Ids.Device(1), "10.0.0.1", DiscoverySource.Mdns), TimeSpan.Zero));

        Assert.Equal(AnnouncementNovelty.Changed, cache.Observe(Claim(Ids.Device(1), "10.0.0.7", DiscoverySource.Broadcast), TimeSpan.FromSeconds(1)));

        Assert.True(cache.TryGet(Ids.Device(1), out Announcement? held));
        Assert.Equal(new IPEndPoint(IPAddress.Parse("10.0.0.7"), 41810), held.Address);
        Assert.Equal(1, cache.Count);
    }

    // The same claim heard by a second mechanism adds nothing to pass on; the cache records both mechanisms.
    [Fact]
    public void TheSameClaimFromAnotherMechanismIsADuplicate_AndItsSourceIsMerged()
    {
        var cache = new AnnouncementCache(capacity: 8, repeatAfter: Minute);
        cache.Observe(Claim(Ids.Device(1), "10.0.0.1", DiscoverySource.Mdns), TimeSpan.Zero);

        Assert.Equal(AnnouncementNovelty.Duplicate, cache.Observe(Claim(Ids.Device(1), "10.0.0.1", DiscoverySource.Broadcast), TimeSpan.FromSeconds(1)));

        Assert.True(cache.TryGet(Ids.Device(1), out Announcement? held));
        Assert.Equal(DiscoverySource.Mdns | DiscoverySource.Broadcast, held.Source);
    }

    [Fact]
    public void TheSameClaimIsPassedOnAgainOnlyAfterTheRepeatWindow()
    {
        var cache = new AnnouncementCache(capacity: 8, repeatAfter: Minute);
        Announcement claim = Claim(Ids.Device(1), "10.0.0.1", DiscoverySource.Mdns);
        cache.Observe(claim, TimeSpan.Zero);

        Assert.Equal(AnnouncementNovelty.Duplicate, cache.Observe(claim, Minute - TimeSpan.FromTicks(1)));
        Assert.Equal(AnnouncementNovelty.Repeated, cache.Observe(claim, Minute));
        Assert.Equal(AnnouncementNovelty.Duplicate, cache.Observe(claim, Minute + TimeSpan.FromSeconds(1)));
    }

    // SPEC.md §3.3: a static peer claims no id, so two static peers are told apart by their addresses.
    [Fact]
    public void StaticPeersAreKeptPerAddress()
    {
        var cache = new AnnouncementCache(capacity: 8, repeatAfter: Minute);

        Assert.Equal(AnnouncementNovelty.New, cache.Observe(Announcement.ForStaticPeer(new IPEndPoint(IPAddress.Parse("10.0.0.1"), 41810)), TimeSpan.Zero));
        Assert.Equal(AnnouncementNovelty.New, cache.Observe(Announcement.ForStaticPeer(new IPEndPoint(IPAddress.Parse("10.0.0.2"), 41810)), TimeSpan.Zero));
        Assert.Equal(2, cache.Count);
    }
}
