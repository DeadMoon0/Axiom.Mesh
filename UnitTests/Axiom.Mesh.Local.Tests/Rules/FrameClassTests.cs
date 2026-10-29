using System.Collections.Generic;
using Axiom.Mesh.Local.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// The message classes of LOCAL-SPEC.md §3.3: the id says the class before anything else is read, a class the sender
/// may not send is a violation (§11.3), and an unknown id in the request, event or callback range is not (§1.5).
/// </summary>
public sealed class FrameClassTests
{
    private const string Host = "host";
    private const string Client = "client";

    // Every well-formed frame either side can produce, by message id; ids nobody produces get a four-byte body.
    private static readonly Dictionary<ushort, byte[]> WellFormed = BuildWellFormed();

    [Theory]
    [InlineData(Host, 1, true)] // A client's Hello, Bye, Ping and Pong reach the Host (session class, sent by both).
    [InlineData(Host, 3, true)] // A client's Hello, Bye, Ping and Pong reach the Host (session class, sent by both).
    [InlineData(Host, 4, true)] // A client's Hello, Bye, Ping and Pong reach the Host (session class, sent by both).
    [InlineData(Host, 5, true)] // A client's Hello, Bye, Ping and Pong reach the Host (session class, sent by both).
    [InlineData(Host, 2, false)] // HelloAck and Drain are the Host's; a client sending them is a violation.
    [InlineData(Host, 6, false)] // HelloAck and Drain are the Host's; a client sending them is a violation.
    [InlineData(Host, 7, false)] // 7 to 31: session extensions, a violation when unknown.
    [InlineData(Host, 31, false)] // 7 to 31: session extensions, a violation when unknown.
    [InlineData(Host, 32, false)] // 32 Response: the Host's only.
    [InlineData(Host, 33, true)] // 33 Reply: a service's (the user-role case is its own test).
    [InlineData(Host, 34, false)] // 34 to 255: reserved, a violation.
    [InlineData(Host, 255, false)] // 34 to 255: reserved, a violation.
    [InlineData(Host, 256, true)] // 256 to 1023: requests, known or not; an unknown one is answered, never a violation.
    [InlineData(Host, 518, true)] // 256 to 1023: requests, known or not; an unknown one is answered, never a violation.
    [InlineData(Host, 999, true)] // 256 to 1023: requests, known or not; an unknown one is answered, never a violation.
    [InlineData(Host, 1023, true)] // 256 to 1023: requests, known or not; an unknown one is answered, never a violation.
    [InlineData(Host, 1024, false)] // 1024 to 2047: events and callbacks are the Host's.
    [InlineData(Host, 1100, false)] // 1024 to 2047: events and callbacks are the Host's.
    [InlineData(Host, 1537, false)] // 1024 to 2047: events and callbacks are the Host's.
    [InlineData(Host, 0, false)] // 0 and 2048 to 65535: never assigned.
    [InlineData(Host, 2048, false)] // 0 and 2048 to 65535: never assigned.
    [InlineData(Host, 65535, false)] // 0 and 2048 to 65535: never assigned.
    [InlineData(Client, 2, true)] // The Host's HelloAck, Bye, Ping, Pong and Drain reach a client.
    [InlineData(Client, 3, true)] // The Host's HelloAck, Bye, Ping, Pong and Drain reach a client.
    [InlineData(Client, 4, true)] // The Host's HelloAck, Bye, Ping, Pong and Drain reach a client.
    [InlineData(Client, 5, true)] // The Host's HelloAck, Bye, Ping, Pong and Drain reach a client.
    [InlineData(Client, 6, true)] // The Host's HelloAck, Bye, Ping, Pong and Drain reach a client.
    [InlineData(Client, 1, false)] // A Hello and an unknown session id are violations from the Host.
    [InlineData(Client, 7, false)] // A Hello and an unknown session id are violations from the Host.
    [InlineData(Client, 32, true)] // 32 Response reaches a client; 33 Reply is a service's, never the Host's.
    [InlineData(Client, 33, false)] // 32 Response reaches a client; 33 Reply is a service's, never the Host's.
    [InlineData(Client, 34, false)] // Reserved and request ids are violations from the Host.
    [InlineData(Client, 256, false)] // Reserved and request ids are violations from the Host.
    [InlineData(Client, 999, false)] // Reserved and request ids are violations from the Host.
    [InlineData(Client, 1024, true)] // Events, known or not, and callbacks, known or not, reach a client.
    [InlineData(Client, 1034, true)] // Events, known or not, and callbacks, known or not, reach a client.
    [InlineData(Client, 1100, true)] // Events, known or not, and callbacks, known or not, reach a client.
    [InlineData(Client, 1536, true)] // Events, known or not, and callbacks, known or not, reach a client.
    [InlineData(Client, 1537, true)] // Events, known or not, and callbacks, known or not, reach a client.
    [InlineData(Client, 2047, true)] // Events, known or not, and callbacks, known or not, reach a client.
    [InlineData(Client, 0, false)] // Never assigned.
    [InlineData(Client, 2048, false)] // Never assigned.
    public void AFrameOfEachClassFromEachSideIsAcceptedOrAViolationAsTheClassRulesSay(string reader, ushort id, bool accepted)
    {
        byte[] frame = WellFormed.TryGetValue(id, out byte[]? known) ? known : Frames.Raw(id, [0, 0, 0, 1]);

        DecodeStatus status = reader == Host ? Frames.AtHost(frame).Status : Frames.AtClient(frame).Status;

        Assert.Equal(accepted ? DecodeStatus.Message : DecodeStatus.Invalid, status);
    }

    [Fact]
    public void AReplyIsAViolationFromAUserClient_AndAcceptedFromAService()
    {
        byte[] reply = Frames.FromClient(Reply.ForUnknownCallback(4));

        foreach ((Role role, DecodeStatus expected) in new[] { (Role.User, DecodeStatus.Invalid), (Role.Service, DecodeStatus.Message) })
        {
            using var host = new CodecRunner<IClientMessage, IHostMessage>(LocalProtocol.HostSide);
            host.Encode(ScriptedHost.Ack(role), new System.Buffers.ArrayBufferWriter<byte>());

            Assert.Equal(expected, Frames.AtHost(host, reply).Status);
        }
    }

    [Fact]
    public void TheClassIsKnownFromTheIdAlone()
    {
        Assert.Equal(MessageClass.Unassigned, LocalMessageIds.ClassOf(0));
        Assert.Equal(MessageClass.Session, LocalMessageIds.ClassOf(1));
        Assert.Equal(MessageClass.Session, LocalMessageIds.ClassOf(31));
        Assert.Equal(MessageClass.Response, LocalMessageIds.ClassOf(32));
        Assert.Equal(MessageClass.Reply, LocalMessageIds.ClassOf(33));
        Assert.Equal(MessageClass.Reserved, LocalMessageIds.ClassOf(34));
        Assert.Equal(MessageClass.Reserved, LocalMessageIds.ClassOf(255));
        Assert.Equal(MessageClass.Request, LocalMessageIds.ClassOf(256));
        Assert.Equal(MessageClass.Request, LocalMessageIds.ClassOf(1023));
        Assert.Equal(MessageClass.Event, LocalMessageIds.ClassOf(1024));
        Assert.Equal(MessageClass.Event, LocalMessageIds.ClassOf(1535));
        Assert.Equal(MessageClass.Callback, LocalMessageIds.ClassOf(1536));
        Assert.Equal(MessageClass.Callback, LocalMessageIds.ClassOf(2047));
        Assert.Equal(MessageClass.Unassigned, LocalMessageIds.ClassOf(2048));
        Assert.Equal(MessageClass.Unassigned, LocalMessageIds.ClassOf(ushort.MaxValue));
    }

    private static Dictionary<ushort, byte[]> BuildWellFormed()
    {
        var frames = new Dictionary<ushort, byte[]>();
        foreach (IClientMessage message in MessageSamples.FromClient.Values)
            frames.TryAdd(message.MessageId, Frames.FromClient(message));
        foreach (IHostMessage message in MessageSamples.FromHost.Values)
            frames.TryAdd(message.MessageId, Frames.FromHost(message));

        // Ids at the ends of their ranges, with a well-formed envelope.
        frames[1023] = Frames.FromClient(new UnknownRequest { Id = 1023, Correlation = 9 });
        frames[1536] = Frames.FromHost(new UnknownCallback { Id = 1536, CallbackId = 9 });
        frames[2047] = Frames.FromHost(new UnknownCallback { Id = 2047, CallbackId = 9 });
        return frames;
    }
}
