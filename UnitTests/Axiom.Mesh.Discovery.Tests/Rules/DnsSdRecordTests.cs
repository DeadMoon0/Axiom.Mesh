using System;
using System.Collections.Generic;
using System.Net;
using Axiom.Mesh.Discovery.Tests.Support;

namespace Axiom.Mesh.Discovery.Tests.Rules;

/// <summary>SPEC.md §3.1: the DNS-SD instance name and TXT keys, and which records a node ignores.</summary>
public sealed class DnsSdRecordTests
{
    private const string Instance = "0102030405060708090a0b0c0d0e0f10";
    private const string DeviceHex = "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
    private const string UserHex = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly IPAddress Source = IPAddress.Parse("192.168.1.20");

    // §3.1: "the instance name equal to the first 32 hex characters of its device id (a DNS-SD instance name is at most
    // 63 bytes)"; §1.3: "Hex is lowercase."
    [Fact]
    public void TheInstanceNameIsTheFirst32LowercaseHexCharactersOfTheDeviceId()
    {
        Assert.Equal(Instance, DnsSdCodec.InstanceName(Ids.CountingDevice()));
        Assert.Equal("abababababababababababababababab", DnsSdCodec.InstanceName(Ids.Device(0xAB)));
    }

    // §3.1: TXT "v" = "1", "d" = device id, hex, 64 characters, "u" = user id, hex, 64 characters; the port of the mesh
    // listener in the record. The claim is at the address the packet came from.
    [Fact]
    public void AWellFormedRecordClaimsItsDeviceAndUserAtTheSourceAddressAndTheSrvPort()
    {
        byte[] packet = DnsPacket.Response(Instance, 41810, "v=1", "d=" + DeviceHex, "u=" + UserHex);

        Announcement claim = Assert.Single(DnsSdCodec.ReadAnnouncements(packet, Source));

        Assert.Equal(Ids.CountingDevice(), claim.Device);
        Assert.Equal(Ids.User(0xAA), claim.User);
        Assert.Equal(new IPEndPoint(Source, 41810), claim.Address);
        Assert.Equal(DiscoverySource.Mdns, claim.Source);
    }

    // §3.1: "A record whose v is not 1, or whose d does not start with the instance name, MUST be ignored."
    [Theory]
    // v is 2.
    [InlineData("v=2", "d=" + DeviceHex)]
    // v is 1 followed by more.
    [InlineData("v=10", "d=" + DeviceHex)]
    // v is missing.
    [InlineData("x=1", "d=" + DeviceHex)]
    // v has no value at all.
    [InlineData("v", "d=" + DeviceHex)]
    // d belongs to another device: its first 32 hex differ from the instance name.
    [InlineData("v=1", "d=ff02030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20")]
    public void ARecordWhoseVIsNot1OrWhoseDDoesNotStartWithTheInstanceNameIsIgnored(string version, string device)
    {
        byte[] packet = DnsPacket.Response(Instance, 41810, version, device, "u=" + UserHex);

        Assert.Empty(DnsSdCodec.ReadAnnouncements(packet, Source));
    }

    // §3.1: "d: device id, hex, 64 characters; u: user id, hex, 64 characters" and the instance name is the first 32 hex
    // characters: a record that is not of this form names no dialable device and is ignored.
    [Theory]
    // d is 63 characters.
    [InlineData(Instance, "d=" + "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f2", "u=" + UserHex)]
    // d holds a character that is not hex.
    [InlineData(Instance, "d=" + "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1fzz", "u=" + UserHex)]
    // u is missing.
    [InlineData(Instance, "d=" + DeviceHex, "x=" + UserHex)]
    // u is 65 characters.
    [InlineData(Instance, "d=" + DeviceHex, "u=" + UserHex + "a")]
    // The instance name is shorter than 32 characters, though d starts with it.
    [InlineData("0102030405060708", "d=" + DeviceHex, "u=" + UserHex)]
    // The instance name is a human name.
    [InlineData("Living room", "d=" + DeviceHex, "u=" + UserHex)]
    // The device id is all zero, which only a static peer claims, though the instance name matches it.
    [InlineData("00000000000000000000000000000000", "d=" + "0000000000000000000000000000000000000000000000000000000000000000", "u=" + UserHex)]
    // The user id is all zero.
    [InlineData(Instance, "d=" + DeviceHex, "u=" + "0000000000000000000000000000000000000000000000000000000000000000")]
    public void ARecordWhoseIdsOrInstanceNameAreNotOfTheSpecifiedFormIsIgnored(string instance, string device, string user)
    {
        byte[] packet = DnsPacket.Response(instance, 41810, "v=1", device, user);

        Assert.Empty(DnsSdCodec.ReadAnnouncements(packet, Source));
    }

    // §3.1: "Unknown keys MUST be ignored." RFC 6763 §6.4: keys compare without case, and the first of a key counts.
    [Fact]
    public void UnknownKeysAreIgnored_KeysCompareWithoutCase_TheFirstOfAKeyCounts()
    {
        byte[] packet = DnsPacket.Response(Instance, 41810, "path=/mesh", "V=1", "v=9", "colour", "D=" + DeviceHex.ToUpperInvariant(), "u=" + UserHex, "vv=1");

        Announcement claim = Assert.Single(DnsSdCodec.ReadAnnouncements(packet, Source));

        Assert.Equal(Ids.CountingDevice(), claim.Device);
    }

    // §3.1: a node announces the service and answers a query for it; every other question is somebody else's.
    [Fact]
    public void OnlyAQueryForTheServiceTypeAsksANodeToAnswer()
    {
        Assert.True(DnsSdCodec.IsServiceQuery(DnsSdCodec.WriteQuery()));

        byte[] otherService = DnsSdCodec.WriteQuery();
        otherService[14] = (byte)'b';
        Assert.False(DnsSdCodec.IsServiceQuery(otherService));

        byte[] response = DnsPacket.Response(Instance, 41810, "v=1", "d=" + DeviceHex, "u=" + UserHex);
        Assert.False(DnsSdCodec.IsServiceQuery(response));
        Assert.Empty(DnsSdCodec.ReadAnnouncements(DnsSdCodec.WriteQuery(), Source));
    }

    // §3.1: what a node announces is what another node reads: the instance name, v=1, d, u and the mesh port.
    [Fact]
    public void WhatANodeAnnouncesIsWhatAnotherNodeReads()
    {
        var self = new SelfRecord { Device = Ids.CountingDevice(), User = Ids.User(0xAA), Port = 41810 };
        List<IPAddress> addresses = [IPAddress.Parse("192.168.1.20"), IPAddress.Parse("fe80::1")];

        Announcement claim = Assert.Single(DnsSdCodec.ReadAnnouncements(DnsSdCodec.WriteAnswer(self, addresses), Source));

        Assert.Equal(new Announcement(self.Device, self.User, new IPEndPoint(Source, 41810), DiscoverySource.Mdns), claim);
    }

    // RFC 6762 §10.1: a record with TTL 0 is a goodbye; it claims nothing.
    [Fact]
    public void AGoodbyeRecordClaimsNothing()
    {
        byte[] packet = DnsPacket.Response(Instance, 41810, "v=1", "d=" + DeviceHex, "u=" + UserHex);
        byte[] goodbye = (byte[])packet.Clone();

        // Both records' TTLs are the 4 bytes before their data length: zero them where the builder wrote 120.
        for (int i = 0; i + 1 < goodbye.Length; i++)
        {
            if (goodbye[i] == 0x80 && goodbye[i + 1] == 0x01)
                goodbye.AsSpan(i + 2, 4).Clear();
        }

        Assert.Single(DnsSdCodec.ReadAnnouncements(packet, Source));
        Assert.Empty(DnsSdCodec.ReadAnnouncements(goodbye, Source));
    }

    // A record's address is a hint that something listens there: the SRV port must be dialable.
    [Fact]
    public void ARecordWithPortZeroIsIgnored()
    {
        byte[] packet = DnsPacket.Response(Instance, 0, "v=1", "d=" + DeviceHex, "u=" + UserHex);

        Assert.Empty(DnsSdCodec.ReadAnnouncements(packet, Source));
    }

    // ARCHITECTURE.md rule 5: every bound is enforced where the values enter; an answer stays far below 9000 bytes.
    [Fact]
    public void AnAnswerRefusesMoreAddressesThanItCarries()
    {
        var self = new SelfRecord { Device = Ids.CountingDevice(), User = Ids.User(0xAA), Port = 41810 };
        var addresses = new List<IPAddress>();
        for (int i = 0; i <= DnsSdCodec.MaxAddresses; i++)
            addresses.Add(IPAddress.Parse("10.0.0." + (i + 1)));

        Assert.Throws<ArgumentException>(() => DnsSdCodec.WriteAnswer(self, addresses));
    }
}
