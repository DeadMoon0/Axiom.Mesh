using System;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Codecs;
using Axiom.ProtoStream.Framing;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>axiom-mesh-pair/1</c>, the short exchange that pairs two users or enrols a device (PAIR-SPEC.md): its name,
/// version, message ids, states and the ProtoStream <see cref="Definition"/> both sides run.
/// </summary>
public static class PairProtocol
{
    /// <summary>The ALPN a pairing connection negotiates, and the protocol's name (PAIR-SPEC.md §4, SPEC.md §14.4).</summary>
    public const string Alpn = "axiom-mesh-pair/1";

    /// <summary>The protocol version a <c>PairHello</c> carries (PAIR-SPEC.md §5.2).</summary>
    public const ushort Version = 1;

    /// <summary>The message id of <see cref="PairHello"/> (PAIR-SPEC.md §11.1).</summary>
    public const ushort PairHelloId = 1;

    /// <summary>The message id of <see cref="PairConfirm"/> (PAIR-SPEC.md §11.1).</summary>
    public const ushort PairConfirmId = 2;

    /// <summary>The message id of <see cref="PairCancel"/> (PAIR-SPEC.md §11.1).</summary>
    public const ushort PairCancelId = 3;

    /// <summary>The state after the TLS handshake: each side sends <c>PairHello</c> first; only <c>PairHello</c> and <c>PairCancel</c> are accepted.</summary>
    public const string Connected = "Connected";

    /// <summary>The state after the peer's <c>PairHello</c>: the users compare the short authentication string; either side may confirm or cancel.</summary>
    public const string Exchanged = "Exchanged";

    /// <summary>The state after the peer's <c>PairConfirm</c>: this side may still confirm or cancel; nothing more is accepted but <c>PairCancel</c>.</summary>
    public const string PeerConfirmed = "PeerConfirmed";

    /// <summary>The final state, entered on a <c>PairCancel</c> sent or received.</summary>
    public const string Closed = "Closed";

    /// <summary>What ProtoStream may buffer beyond the largest frame: its frame-header allowance.</summary>
    private const int FrameBufferAllowance = 64;

    /// <summary>
    /// The definition of one pairing session, built once; both sides use it (PAIR-SPEC.md §5.1). Frames as AMP/1: a
    /// <c>u32</c> length of 2 to 4 198 400, a <c>u16</c> id, the body; any id but 1 to 3 is a violation. No
    /// <c>PairHello</c> within 10 seconds, or nothing for the 10 minutes a pairing may take, ends it with
    /// <c>PairCancel(Expired)</c>; any other violation with <c>PairCancel(Violation)</c>.
    /// </summary>
    public static ProtocolDefinition<PairMessage, PairMessage> Definition { get; } =
        global::Axiom.ProtoStream.Protocol.Describe<PairMessage, PairMessage>(Alpn)
            .Framing(Framers.LengthPrefixed(LengthPrefix.UInt32BigEndian, Limits.MaxFrameLength))
            .Messages(m => m
                .Discriminator(Discriminator.UInt16BigEndian)
                .Add<PairHello>(PairHelloId)
                .Add<PairConfirm>(PairConfirmId)
                .Add<PairCancel>(PairCancelId))
            .States(s => s
                .Start(Connected)
                .In(Connected)
                    .On<PairHello>().Delegate().GoTo(Exchanged)
                    .On<PairCancel>().Delegate().GoTo(Closed)
                    .OnSend<PairHello>()
                    .OnSend<PairCancel>().GoTo(Closed)
                .In(Exchanged)
                    .On<PairConfirm>().Delegate().GoTo(PeerConfirmed)
                    .On<PairCancel>().Delegate().GoTo(Closed)
                    .OnSend<PairConfirm>()
                    .OnSend<PairCancel>().GoTo(Closed)
                .In(PeerConfirmed)
                    .On<PairCancel>().Delegate().GoTo(Closed)
                    .OnSend<PairConfirm>()
                    .OnSend<PairCancel>().GoTo(Closed)
                .Final(Closed))
            .Limits(l => l
                .FirstMessageTimeout(TimeSpan.FromSeconds(Limits.FirstHelloSeconds))
                .IdleTimeout(TimeSpan.FromSeconds(Limits.PairingSeconds))
                .MaxBufferedBytes(Limits.MaxFrameLength + FrameBufferAllowance))
            .ReplyToViolations(PairCancel.ForViolation)
            .Flushing(FlushPolicy.EveryWrite)
            .Build();
}
