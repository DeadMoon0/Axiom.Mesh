using System;
using System.Collections.Generic;
using System.Net;
using Axiom.Mesh.Discovery.Tests.Support;

namespace Axiom.Mesh.Discovery.Tests.Regression;

/// <summary>The parsers are the attack surface on the LAN: any bytes are read or refused, never thrown on.</summary>
public sealed class ParserFuzzTests
{
    private const int Iterations = 20_000;

    private static readonly IPAddress Source = IPAddress.Parse("192.168.1.20");

    // Damage: an exception in a parser would end a shell's receive loop, so one hostile datagram on the LAN would
    // silence discovery for the node until it restarts.
    [Fact]
    public void MutatedDnsPacketsAreReadOrRefusedButNeverThrow_Seed42()
    {
        var self = new SelfRecord { Device = Ids.CountingDevice(), User = Ids.User(0xAA), Port = 41810 };
        byte[][] samples =
        [
            Convert.FromHexString(DnsVectors.Answer),
            Convert.FromHexString(DnsVectors.Captured),
            DnsSdCodec.WriteQuery(),
            DnsSdCodec.WriteAnswer(self, [IPAddress.Parse("10.0.0.1")]),
        ];

        int read = 0;
        var random = new Random(42);
        for (int i = 0; i < Iterations; i++)
        {
            byte[] packet = Mutate(samples[random.Next(samples.Length)], random);
            read += DnsSdCodec.ReadAnnouncements(packet, Source).Count;
            DnsSdCodec.IsServiceQuery(packet);
        }

        Assert.True(read > 0, "No mutation was still read; the fuzzer did not reach the well-formed paths.");
    }

    // Damage: as above, for the broadcast listener; its datagram is fixed, so mutations mostly hit the refusals.
    [Fact]
    public void MutatedBroadcastDatagramsAreReadOrRefusedButNeverThrow_Seed42()
    {
        byte[][] samples = [BroadcastCodec.Write(BroadcastKind.Announcement, Ids.Self(0x11, 0x22, 41810)), BroadcastCodec.Write(BroadcastKind.Query, Ids.Self(0x33, 0x44, 1))];

        int read = 0;
        int refused = 0;
        var random = new Random(42);
        for (int i = 0; i < Iterations; i++)
        {
            if (BroadcastCodec.TryParse(Mutate(samples[random.Next(samples.Length)], random), out _))
                read++;
            else
                refused++;
        }

        Assert.True(read > 0 && refused > 0, $"read {read}, refused {refused}: the fuzzer reached only one side.");
    }

    // Damage: a name decoder that follows a pointer forwards or to itself would loop for ever on one packet of 30 bytes.
    [Fact]
    public void ACompressionPointerToItselfOrForwardsIsRefusedNotFollowed()
    {
        // Header: a response with one answer; the answer's owner name is a pointer to its own offset (12), then forwards (14).
        byte[] self = [0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0xC0, 12, 0, 16, 0, 1, 0, 0, 0, 120, 0, 0];
        byte[] forwards = [0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0xC0, 14, 0, 16, 0, 1, 0, 0, 0, 120, 0, 0];

        Assert.Empty(DnsSdCodec.ReadAnnouncements(self, Source));
        Assert.Empty(DnsSdCodec.ReadAnnouncements(forwards, Source));
    }

    // Damage: a packet over 9000 bytes or with thousands of records would make one datagram cost unbounded work.
    [Fact]
    public void APacketOverTheBoundsIsRefusedWhole()
    {
        byte[] answer = Convert.FromHexString(DnsVectors.Answer);
        byte[] oversized = [.. answer, .. new byte[DnsSdCodec.MaxPacketLength - answer.Length + 1]];
        byte[] tooManyRecords = (byte[])answer.Clone();
        tooManyRecords[11] = DnsSdCodec.MaxRecords;

        Assert.Single(DnsSdCodec.ReadAnnouncements(answer, Source));
        Assert.Empty(DnsSdCodec.ReadAnnouncements(oversized, Source));
        Assert.Empty(DnsSdCodec.ReadAnnouncements(tooManyRecords, Source));
    }

    private static byte[] Mutate(byte[] sample, Random random)
    {
        byte[] bytes = (byte[])sample.Clone();
        switch (random.Next(5))
        {
            case 0:
                bytes[random.Next(bytes.Length)] ^= (byte)(1 << random.Next(8));
                return bytes;
            case 1:
                return bytes[..random.Next(bytes.Length)];
            case 2:
                bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
                return bytes;
            case 3:
                // A compression pointer or a length byte where a label was.
                bytes[random.Next(bytes.Length)] = (byte)(0xC0 | random.Next(64));
                return bytes;
            default:
                var grown = new List<byte>(bytes);
                grown.Insert(random.Next(grown.Count), (byte)random.Next(256));
                return [.. grown];
        }
    }
}
