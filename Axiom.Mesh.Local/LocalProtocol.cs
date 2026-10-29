using System;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Framing;

namespace Axiom.Mesh.Local;

/// <summary>
/// <c>axiom-mesh-local/1</c>, the protocol every program on a device speaks to the Host over the pipe
/// (LOCAL-SPEC.md), as two ProtoStream definitions: <see cref="HostSide"/> for the Host's local port and
/// <see cref="ClientSide"/> for every client. Both share one framer (a <c>u32</c> big-endian length of 2 to 4 198 400
/// bytes, §3.1) and one frame codec that classifies by id range (§3.3).
/// </summary>
/// <remarks>
/// <para>
/// States (§4.1): <c>Connected</c> until <c>HelloAck</c>, then <c>Ready</c>; <c>Bye</c> from either side ends in
/// <c>Closed</c>. Draining (§4.5) is the Host's own state of mind, not a state of the definition: everything of Ready
/// still flows.
/// </para>
/// <para>
/// Timeouts: the first message must arrive within 10 seconds (§2.3; on the client side that is <c>HelloAck</c>). After
/// it, liveness is the Host's Ping/Pong (§4.4): the Host pings after 60 seconds without receiving and a client answers
/// within 30, so neither side is ever 60 + 30 + 30 seconds (one frame's completion, §3.1) without receiving anything
/// from a live peer. The idle timeout is set to exactly that, 120 seconds, as a backstop that cannot close a responsive
/// peer; the Host still closes an unresponsive client after 90 seconds with <c>Bye(Unresponsive)</c> on its own clock.
/// </para>
/// <para>
/// Answered without the application (§1.5, §4.4): a <c>Ping</c> on both sides, an <see cref="UnknownRequest"/> on the
/// Host side (status <c>UnknownRequest</c>, connection kept), an <see cref="UnknownCallback"/> on the client side
/// (<c>Reply(UnknownRequest)</c>). An <see cref="UnknownEvent"/> is read and discarded.
/// </para>
/// </remarks>
public sealed class LocalProtocol
{
    private LocalProtocol()
    {
    }

    /// <summary>The protocol's name and version (§1.5).</summary>
    public const string Name = "axiom-mesh-local/1";

    /// <summary>The version carried in <c>Hello</c> and <c>HelloAck</c> (§1.5).</summary>
    public const ushort Version = 1;

    private const string Connected = "Connected";
    private const string Ready = "Ready";
    private const string Closed = "Closed";

    /// <summary>The idle backstop: Ping after 60 s, Pong within 30 s, one frame completed within 30 s (§3.1, §4.4).</summary>
    private static readonly TimeSpan IdleBackstop = TimeSpan.FromSeconds(Limits.LocalPingAfterSeconds + Limits.LocalPongSeconds + Limits.FrameCompletionSeconds);

    /// <summary>Room for the largest frame and its length prefix in the session's buffer.</summary>
    private const int BufferedBytes = Limits.MaxFrameLength + 64;

    /// <summary>The Host's side: reads what clients send, writes what the Host sends.</summary>
    public static ProtocolDefinition<IClientMessage, IHostMessage> HostSide { get; } = BuildHostSide();

    /// <summary>A client's side: reads what the Host sends, writes what clients send.</summary>
    public static ProtocolDefinition<IHostMessage, IClientMessage> ClientSide { get; } = BuildClientSide();

    private static IFramer Framer() => Framers.LengthPrefixed(LengthPrefix.UInt32BigEndian, Limits.MaxFrameLength);

    private static ProtocolDefinition<IClientMessage, IHostMessage> BuildHostSide() =>
        Protocol.Describe<IClientMessage, IHostMessage>(Name)
            .Framing(Framer())
            .FrameCodec(() => new LocalFrameCodec<IClientMessage, IHostMessage>(LocalSide.Host))
            .States(s => s
                .Start(Connected)
                .In(Connected)
                    .On<Hello>().Delegate()
                    .On<Bye>().Delegate().GoTo(Closed)
                    .OnSend<HelloAck>().GoTo(Ready)
                    .OnSend<Bye>().GoTo(Closed)
                .In(Ready)
                    .On<RequestFrame>().Delegate()
                    .On<UnknownRequest>().Respond(request => Response.ForUnknownRequest(request.Correlation))
                    .On<Reply>().Delegate()
                    .On<Ping>().Respond(ping => ping.Answer())
                    .On<Pong>().Delegate()
                    .On<Bye>().Delegate().GoTo(Closed)
                    .OnSend<Response>()
                    .OnSend<EventFrame>()
                    .OnSend<Callback>()
                    .OnSend<Ping>()
                    .OnSend<Drain>()
                    .OnSend<Bye>().GoTo(Closed)
                .Final(Closed))
            .Limits(l => l
                .FirstMessageTimeout(TimeSpan.FromSeconds(Limits.LocalHelloSeconds))
                .IdleTimeout(IdleBackstop)
                .MaxBufferedBytes(BufferedBytes))
            .ReplyToViolations(HostReply)
            .OnClose(() => Bye.Of(ByeReason.Shutdown, ""))
            .Build();

    private static ProtocolDefinition<IHostMessage, IClientMessage> BuildClientSide() =>
        Protocol.Describe<IHostMessage, IClientMessage>(Name)
            .Framing(Framer())
            .FrameCodec(() => new LocalFrameCodec<IHostMessage, IClientMessage>(LocalSide.Client))
            .States(s => s
                .Start(Connected)
                .In(Connected)
                    .OnSend<Hello>()
                    .On<HelloAck>().Delegate().GoTo(Ready)
                    .On<Bye>().Delegate().GoTo(Closed)
                    .OnSend<Bye>().GoTo(Closed)
                .In(Ready)
                    .On<Response>().Delegate()
                    .On<EventFrame>().Delegate()
                    .On<UnknownEvent>().Wait()
                    .On<Callback>().Delegate()
                    .On<UnknownCallback>().Respond(callback => Reply.ForUnknownCallback(callback.CallbackId))
                    .On<Ping>().Respond(ping => ping.Answer())
                    .On<Pong>().Delegate()
                    .On<Drain>().Delegate()
                    .On<Bye>().Delegate().GoTo(Closed)
                    .OnSend<RequestFrame>()
                    .OnSend<UnknownRequest>()
                    .OnSend<Reply>()
                    .OnSend<Ping>()
                    .OnSend<Bye>().GoTo(Closed)
                .Final(Closed))
            .Limits(l => l
                .FirstMessageTimeout(TimeSpan.FromSeconds(Limits.LocalHelloSeconds))
                .IdleTimeout(IdleBackstop)
                .MaxBufferedBytes(BufferedBytes))
            .ReplyToViolations(ClientReply)
            .OnClose(() => Bye.Of(ByeReason.Done, ""))
            .Build();

    // §2.3: no Hello within 10 seconds closes without a frame. §4.4: a client that stopped answering is Unresponsive.
    // Everything else is a violation (§11.3).
    private static IHostMessage? HostReply(Violation violation) => violation switch
    {
        { Code: ViolationCode.Timeout, State: Connected } => null,
        { Code: ViolationCode.Timeout } => Bye.Of(ByeReason.Unresponsive, violation.Detail),
        _ => Bye.Of(ByeReason.Violation, violation.Detail),
    };

    // A Host defect is a lost connection to the client (§11.3); the Bye tells the Host's log why.
    private static IClientMessage ClientReply(Violation violation) =>
        Bye.Of(ByeReason.Unspecified, "Host defect: " + violation.Detail);
}
