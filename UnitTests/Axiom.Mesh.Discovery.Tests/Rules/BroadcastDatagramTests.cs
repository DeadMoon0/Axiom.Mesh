using System;
using System.Linq;
using System.Net;
using Axiom.Mesh.Discovery.Tests.Support;

namespace Axiom.Mesh.Discovery.Tests.Rules;

/// <summary>SPEC.md §3.2: the UDP broadcast datagram and what a listener ignores.</summary>
public sealed class BroadcastDatagramTests
{
    // §3.2: magic "AMD1" (4), flags u8, deviceId id (32), userId id (32), port u16: 71 bytes, big-endian.
    private static byte[] Datagram(byte flags) =>
        [.. "AMD1"u8.ToArray(), flags, .. Enumerable.Repeat((byte)0x11, 32), .. Enumerable.Repeat((byte)0x22, 32), 0xA3, 0x52];

    // §3.2: "A node ... MUST ignore datagrams of any other length, magic or flag bits."
    [Theory]
    // One byte short.
    [InlineData("short")]
    // One byte long.
    [InlineData("long")]
    // Empty.
    [InlineData("empty")]
    // Another magic, as a later version would use.
    [InlineData("AMD2")]
    // Another magic in lower case.
    [InlineData("amd1")]
    // Bit 1 set beside the announcement bit.
    [InlineData("flags 0x03")]
    // Bit 7 set on a query.
    [InlineData("flags 0x80")]
    // Every bit set.
    [InlineData("flags 0xFF")]
    public void ADatagramOfAnotherLengthMagicOrFlagBitsIsIgnored(string variant)
    {
        byte[] datagram = variant switch
        {
            "short" => Datagram(0x01)[..70],
            "long" => [.. Datagram(0x01), 0x00],
            "empty" => [],
            "AMD2" => [.. "AMD2"u8.ToArray(), .. Datagram(0x01)[4..]],
            "amd1" => [.. "amd1"u8.ToArray(), .. Datagram(0x01)[4..]],
            "flags 0x03" => Datagram(0x03),
            "flags 0x80" => Datagram(0x80),
            "flags 0xFF" => Datagram(0xFF),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };

        Assert.False(BroadcastCodec.TryParse(datagram, out _));
        Assert.True(BroadcastCodec.TryParse(Datagram(0x01), out _), "The unaltered datagram must be accepted, or the refusals above prove nothing.");
    }

    // §3.2: "flags: bit 0: 1 = announcement, 0 = query"; both carry the sender's own ids and the port of its mesh listener.
    [Fact]
    public void BitZeroTellsAnAnnouncementFromAQuery_BothCarryTheSendersIdsAndMeshPort()
    {
        Assert.True(BroadcastCodec.TryParse(Datagram(0x01), out BroadcastDatagram announcement));
        Assert.True(BroadcastCodec.TryParse(Datagram(0x00), out BroadcastDatagram query));

        Assert.Equal(BroadcastKind.Announcement, announcement.Kind);
        Assert.Equal(BroadcastKind.Query, query.Kind);
        foreach (BroadcastDatagram datagram in new[] { announcement, query })
        {
            Assert.Equal(Ids.Device(0x11), datagram.Device);
            Assert.Equal(Ids.User(0x22), datagram.User);
            Assert.Equal(41810, datagram.Port);
        }
    }

    // §3.2: "A query ... asks every listener to answer with an announcement, sent unicast to the query's source"; what a
    // node writes is what a listener reads, for both kinds.
    [Fact]
    public void WhatANodeWritesIsTheDatagramAListenerReads()
    {
        SelfRecord self = Ids.Self(0x33, 0x44, 41810);
        foreach (BroadcastKind kind in new[] { BroadcastKind.Query, BroadcastKind.Announcement })
        {
            byte[] written = BroadcastCodec.Write(kind, self);

            Assert.Equal(71, written.Length);
            Assert.True(BroadcastCodec.TryParse(written, out BroadcastDatagram read));
            Assert.Equal(new BroadcastDatagram(kind, self.Device, self.User, self.Port), read);
        }
    }

    // §3: a claim is a hint that names an address to dial. An all-zero id is no device (only a static peer claims it)
    // and port 0 cannot be dialled, so neither may come off the network.
    [Theory]
    // The device id is all zero.
    [InlineData(5)]
    // The user id is all zero.
    [InlineData(37)]
    // The port is 0.
    [InlineData(69)]
    public void ADatagramClaimingAZeroIdOrPortZeroIsIgnored(int zeroedFrom)
    {
        byte[] datagram = Datagram(0x01);
        int length = zeroedFrom == 69 ? 2 : 32;
        datagram.AsSpan(zeroedFrom, length).Clear();

        Assert.False(BroadcastCodec.TryParse(datagram, out _));
    }

    // §3.2: a query names its sender as much as an announcement does; the claim is at the datagram's source address.
    [Fact]
    public void TheClaimIsAtTheSourceAddressWithTheAnnouncedMeshPort()
    {
        Assert.True(BroadcastCodec.TryParse(Datagram(0x00), out BroadcastDatagram query));

        Announcement claim = query.ToAnnouncement(IPAddress.Parse("192.168.1.20"));

        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.1.20"), 41810), claim.Address);
        Assert.Equal(DiscoverySource.Broadcast, claim.Source);
    }
}
