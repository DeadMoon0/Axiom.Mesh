using System;
using System.Threading;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Framing;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// AMP/1, the protocol two nodes of a mesh speak over one mutually authenticated TLS connection (SPEC.md): its name,
/// version, states and the ProtoStream <see cref="Definition"/> every session runs.
/// </summary>
public sealed class Amp
{
    private Amp()
    {
    }

    /// <summary>The ALPN a mesh connection negotiates, and the protocol's name (SPEC.md §4.2, §14.4).</summary>
    public const string Alpn = "axiom-mesh/1";

    /// <summary>The protocol version a <c>Hello</c> carries (§1.4, §7.1).</summary>
    public const ushort Version = 1;

    /// <summary>
    /// The state after the TLS handshake (§6.1): each side sends <c>Hello</c> first, then may send <c>Ping</c> and
    /// <c>Bye</c>; only <c>Hello</c> and <c>Bye</c> are accepted. The peer's <c>Hello</c> moves the session to
    /// <see cref="Established"/>.
    /// </summary>
    public const string Connected = "Connected";

    /// <summary>
    /// The state after the peer's <c>Hello</c> arrived (§6.1). The session handler checks that <c>Hello</c> with
    /// <see cref="HelloRule.Check"/> before it reads or sends anything else, and closes with the refusal's <c>Bye</c> when it
    /// fails, so that every other message flows only between validated peers.
    /// </summary>
    public const string Established = "Established";

    /// <summary>The final state, entered on a <c>Bye</c> sent or received: nothing more is sent (§6.4).</summary>
    public const string Closed = "Closed";

    /// <summary>
    /// What ProtoStream may buffer beyond the largest frame: its frame-header allowance, so a full 4 MiB + 4 KiB frame
    /// always fits.
    /// </summary>
    private const int FrameBufferAllowance = 64;

    /// <summary>
    /// The definition of one AMP/1 session, built once; the protocol is symmetric, so both ends use it (§6.1).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Frames: a <c>u32</c> big-endian length of 2 to 4 198 400 counting the id and the body, then a <c>u16</c> id
    /// (§5.1); ids 17 to 255 are <see cref="Extension"/>s, consumed silently once established (§1.4).</item>
    /// <item>Connected accepts <c>Hello</c> and <c>Bye</c> and sends <c>Hello</c>, <c>Ping</c> and <c>Bye</c>; Established
    /// accepts and sends every message of §7 but <c>Hello</c>, answers <c>Ping</c> with <c>Pong</c> and every
    /// <c>ServiceOpen</c> with <c>ServiceRefused(unknown)</c> (phase 1 offers no service), and consumes <c>Pong</c>.
    /// <c>ServiceAccepted</c> and <c>ServiceRefused</c> answer a <c>ServiceOpen</c> this side never sends, and are
    /// violations.</item>
    /// <item>A <c>Bye</c> sent or received ends the session in <see cref="Closed"/>; closing a session sends
    /// <c>Bye(Shutdown)</c> unless one was already sent.</item>
    /// <item>No <c>Hello</c> within 10 seconds, or nothing received for 5 minutes, closes with <c>Bye(Idle)</c>; after 60
    /// seconds without sending, a <c>Ping</c> goes out (§6.1, §6.3). Every other violation closes with
    /// <c>Bye(Violation)</c> (§12.1). Every write is flushed.</item>
    /// </list>
    /// </remarks>
    public static ProtocolDefinition<MeshMessage, MeshMessage> Definition { get; } = Build();

    private static ProtocolDefinition<MeshMessage, MeshMessage> Build()
    {
        // The heartbeat's nonce counts up per definition: unique enough to match a Pong, and no clock or randomness (plan §3).
        long heartbeats = 0;
        return global::Axiom.ProtoStream.Protocol.Describe<MeshMessage, MeshMessage>(Alpn)
            .Framing(Framers.LengthPrefixed(LengthPrefix.UInt32BigEndian, Limits.MaxFrameLength))
            .FrameCodec(() => AmpFrameCodec.Instance)
            .States(s => s
                .Start(Connected)
                .In(Connected)
                    .On<Hello>().Delegate().GoTo(Established)
                    .On<Bye>().Delegate().GoTo(Closed)
                    .OnSend<Hello>()
                    .OnSend<Ping>()
                    .OnSend<Bye>().GoTo(Closed)
                .In(Established)
                    .On<Packages>().Delegate()
                    .On<PackageChanged>().Delegate()
                    .On<Have>().Delegate()
                    .On<Want>().Delegate()
                    .On<ObjectMessage>().Delegate()
                    .On<NotFound>().Delegate()
                    .On<Cancel>().Delegate()
                    .On<RosterQuery>().Delegate()
                    .On<RosterNotFound>().Delegate()
                    .On<ServiceOpen>().Respond(open => open.Refuse(ServiceRefusal.Unknown))
                    .On<Ping>().Respond(ping => ping.Answer())
                    .On<Pong>().Wait()
                    .On<Extension>().Wait()
                    .On<Bye>().Delegate().GoTo(Closed)
                    .OnSend<Packages>()
                    .OnSend<PackageChanged>()
                    .OnSend<Have>()
                    .OnSend<Want>()
                    .OnSend<ObjectMessage>()
                    .OnSend<NotFound>()
                    .OnSend<Cancel>()
                    .OnSend<RosterQuery>()
                    .OnSend<RosterNotFound>()
                    .OnSend<Ping>()
                    .OnSend<Bye>().GoTo(Closed)
                .Final(Closed))
            .Limits(l => l
                .FirstMessageTimeout(TimeSpan.FromSeconds(Limits.FirstHelloSeconds))
                .IdleTimeout(TimeSpan.FromSeconds(Limits.IdleSeconds))
                .MaxBufferedBytes(Limits.MaxFrameLength + FrameBufferAllowance))
            .ReplyToViolations(Bye.ForViolation)
            .OnClose(() => new Bye(ByeReason.Shutdown, string.Empty))
            .Heartbeat(TimeSpan.FromSeconds(Limits.PingAfterSilenceSeconds), () => new Ping((ulong)Interlocked.Increment(ref heartbeats)))
            .Flushing(FlushPolicy.EveryWrite)
            .Build();
    }
}
