using System;
using System.Linq;
using Axiom.Mesh.Local.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// The limits of LOCAL-SPEC.md §12 that a frame carries, each accepted at the limit and refused one over: a violation
/// when a client breaks it (§11.3), a Host defect when the Host does. The limits that hold over time (Hello within 10
/// seconds, Pong within 30, queues, operations) are the Host's to enforce; the client's own (64 outstanding requests,
/// 256 queued events) have their own tests.
/// </summary>
public sealed class LimitTests
{
    [Theory]
    [InlineData("frame length")] // 2 to 4 198 400 bytes: a frame of exactly the maximum reads, one byte more is a violation
    [InlineData("frame length below 2")] // a frame too short for its message id is a violation; the shortest message, a Ping, reads
    [InlineData("outstanding requests per connection")] // 64 accepted, the 65th a violation
    [InlineData("page of ListHistory, ListStored")] // 64 Entries; 65 is a Host defect
    [InlineData("page of ListPackages")] // 256 Packages; 257 is a Host defect
    [InlineData("page of ListPins")] // 256 pins; 257 is a Host defect
    [InlineData("points per QueryTelemetry")] // 10 080 points; 10 081 is a Host defect
    [InlineData("Unpin hashes")] // 1 024 hashes in a request; 1 025 a violation
    public void EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit(string limit)
    {
        (bool atLimit, bool oneOver) = limit switch
        {
            "frame length" => (FrameOfLength(Limits.MaxFrameLength), FrameOfLength(Limits.MaxFrameLength + 1)),
            "frame length below 2" => (Frames.AtHost(Frames.Raw(LocalMessageIds.Ping, new byte[8])).Status == DecodeStatus.Message,
                Frames.AtHost([0, 0, 0, 1, 4]).Status == DecodeStatus.Message),
            "outstanding requests per connection" => Outstanding(),
            "page of ListHistory, ListStored" => Page<ListHistory, EntryPage, Entry>(Limits.HistoryPageEntries, more: true),
            "page of ListPackages" => Page<ListPackages, PackagePage, Package>(Limits.PackagePageEntries, more: true),
            "page of ListPins" => Page<ListPins, PinPage, Pin>(Limits.PackagePageEntries, more: true),
            "points per QueryTelemetry" => Page<QueryTelemetry, System.Collections.Generic.IReadOnlyList<TelemetryPoint>, TelemetryPoint>(Limits.MaxTelemetryPoints, more: false),
            "Unpin hashes" => (Unpin(1_024), Unpin(1_025)),
            _ => throw new ArgumentException("No such limit row.", nameof(limit)),
        };

        Assert.True(atLimit, $"{limit}: refused at the limit.");
        Assert.False(oneOver, $"{limit}: accepted one over the limit.");
    }

    // An unknown request whose body pads the frame to the length given, written by hand (no writer of this package
    // produces a frame over the limit): the framer alone decides.
    private static bool FrameOfLength(int length)
    {
        byte[] payload = new byte[length - 2];
        payload[3] = 1;
        byte[] frame = Frames.Raw(900, payload);
        Assert.Equal(length, frame.Length - 4);
        return Frames.AtHost(frame).Status == DecodeStatus.Message;
    }

    private static (bool AtLimit, bool OneOver) Outstanding()
    {
        using var host = new CodecRunner<IClientMessage, IHostMessage>(LocalProtocol.HostSide);
        bool all = Enumerable.Range(1, Limits.MaxOutstandingRequests)
            .All(c => Frames.AtHost(host, Frames.FromClient(new RequestFrame((uint)c, new GetStore()))).Status == DecodeStatus.Message);
        bool over = Frames.AtHost(host, Frames.FromClient(new RequestFrame(1_000, new GetStore()))).Status == DecodeStatus.Message;
        return (all, over);
    }

    // A response whose OK body holds a list of the given length, written by hand so that one over can be written at all.
    private static (bool AtLimit, bool OneOver) Page<TRequest, TOk, TItem>(int maximum, bool more)
        where TRequest : LocalRequest, IRequest<TRequest, TOk>
        where TItem : class, IWireRecord<TItem>
    {
        var item = Samples.Of<TItem>();
        bool Reads(int count)
        {
            byte[] body = Frames.Write((ref WireWriter w) =>
            {
                w.UInt32BigEndian(1);
                w.UInt16BigEndian(0);
                w.UInt16BigEndian((ushort)count);
                for (int i = 0; i < count; i++)
                    item.Write(ref w);
                if (more)
                    w.Boolean(false);
            });
            var response = (Response)Frames.AtClient(Frames.Raw(LocalMessageIds.Response, body)).Message!;
            return response.TryReadOk<TRequest, TOk>(out _);
        }

        return (Reads(maximum), Reads(maximum + 1));
    }

    private static bool Unpin(int count)
    {
        byte[] body = Frames.Write((ref WireWriter w) =>
        {
            w.UInt32BigEndian(1);
            w.UInt16BigEndian((ushort)count);
            for (int i = 0; i < count; i++)
                Samples.Hash(i).Write(ref w);
        });
        return Frames.AtHost(Frames.Raw(Local.Unpin.Id, body)).Status == DecodeStatus.Message;
    }
}
