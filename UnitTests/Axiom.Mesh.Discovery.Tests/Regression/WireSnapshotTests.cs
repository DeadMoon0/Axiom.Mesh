using System;
using System.Collections.Generic;
using System.Net;
using Axiom.Mesh.Discovery.Tests.Support;

namespace Axiom.Mesh.Discovery.Tests.Regression;

/// <summary>The bytes discovery puts on the wire, pinned. Expected values were produced by an independent script, not by the codec.</summary>
public sealed class WireSnapshotTests
{
    // Damage: a reordered field, a little-endian port or a shifted flag bit would still round-trip through this
    // codec and be ignored by every other implementation; the 71 bytes are fixed by SPEC.md §3.2.
    [Fact]
    public void TheBroadcastDatagramIsTheSeventyOneBytesOfTheSpec()
    {
        SelfRecord self = Ids.Self(0x11, 0x22, 41810);
        string ids = new string('1', 64) + new string('2', 64);

        Assert.Equal("414d443101" + ids + "a352", Convert.ToHexString(BroadcastCodec.Write(BroadcastKind.Announcement, self)).ToLowerInvariant());
        Assert.Equal("414d443100" + ids + "a352", Convert.ToHexString(BroadcastCodec.Write(BroadcastKind.Query, self)).ToLowerInvariant());
    }

    // Damage: a changed TTL, a missing cache-flush bit, another record order or a compressed name would change what
    // other responders and browsers (dns-sd, avahi-browse) see of a node, unnoticed by a round trip through this codec.
    [Fact]
    public void TheAnswerForAFixedSelfRecordIsByteForByteStable()
    {
        var self = new SelfRecord { Device = Ids.CountingDevice(), User = Ids.User(0xAA), Port = 41810 };
        List<IPAddress> addresses = [IPAddress.Parse("192.168.1.20"), IPAddress.Parse("fe80::1")];

        Assert.Equal(DnsVectors.Answer, Convert.ToHexString(DnsSdCodec.WriteAnswer(self, addresses)).ToLowerInvariant());
    }

    // Damage: a query of another type or class would be answered by nobody.
    [Fact]
    public void TheQueryIsByteForByteStable()
    {
        Assert.Equal(DnsVectors.Query, Convert.ToHexString(DnsSdCodec.WriteQuery()).ToLowerInvariant());
    }

    // Damage: a reader that only understands this codec's own uncompressed output would miss every node announced by
    // another implementation or relayed by an mDNS proxy: compression pointers, upper-case names, foreign TXT keys, a
    // repeated key, A and NSEC records in the additional section.
    [Fact]
    public void ACapturedResponseWithCompressionAndForeignRecordsIsRead()
    {
        IPAddress source = IPAddress.Parse("192.168.1.20");

        Announcement claim = Assert.Single(DnsSdCodec.ReadAnnouncements(Convert.FromHexString(DnsVectors.Captured), source));

        Assert.Equal(new Announcement(Ids.CountingDevice(), Ids.User(0xAA), new IPEndPoint(source, 41810), DiscoverySource.Mdns), claim);
    }
}
