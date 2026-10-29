using System;
using System.Buffers;
using System.Threading;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Codecs;
using Axiom.ProtoStream.Framing;

namespace Axiom.Mesh.Local;

/// <summary>
/// The frame codec of <c>axiom-mesh-local/1</c> for one side and one session (LOCAL-SPEC.md §3). It reads the message
/// id, classifies the frame by its id range before it parses anything else (§3.3), refuses a class the sender may not
/// send, reads the envelope and then the body by the id's table, and only then makes a message.
/// </summary>
/// <remarks>
/// <para>
/// What is not a violation (§1.5): a request id in the request range that <see cref="RequestTable"/> lacks decodes to an
/// <see cref="UnknownRequest"/>, an unknown event id to an <see cref="UnknownEvent"/> and an unknown callback id to an
/// <see cref="UnknownCallback"/>; the definitions answer or discard them.
/// </para>
/// <para>
/// What the Host side also keeps per session: the correlations of requests read and not yet answered (§3.4: never 0,
/// never twice, at most 64), ended by every <see cref="Response"/> it writes, and the role its <see cref="HelloAck"/>
/// granted, so that a <see cref="Reply"/> from a user client is a violation.
/// </para>
/// </remarks>
/// <typeparam name="TIn">What the side reads: <see cref="IClientMessage"/> for the Host, <see cref="IHostMessage"/> for a client.</typeparam>
/// <typeparam name="TOut">What the side writes.</typeparam>
internal sealed class LocalFrameCodec<TIn, TOut> : IFrameCodec<TIn, TOut>
    where TIn : class
    where TOut : class
{
    private readonly LocalSide side;
    private readonly OutstandingCorrelations outstanding = new();
    private int grantedRole;

    public LocalFrameCodec(LocalSide side) => this.side = side;

    /// <summary>The requests read and not yet answered; meaningful on the Host side.</summary>
    internal OutstandingCorrelations Outstanding => outstanding;

    public DecodeResult Decode(in Frame frame, out TIn message)
    {
        message = null!;
        var reader = new WireReader(frame.Memory);
        ushort id = reader.UInt16BigEndian();
        if (!reader.Ok)
            return DecodeResult.Invalid(ViolationCode.Malformed, "A frame is shorter than its message id (LOCAL-SPEC.md §3.1).");

        DecodeResult result = side == LocalSide.Host
            ? DecodeFromClient(id, ref reader, out LocalMessage? decoded)
            : DecodeFromHost(id, ref reader, out decoded);
        if (result.IsOk)
            message = (TIn)(object)decoded!;
        return result;
    }

    public void Encode(TOut message, IBufferWriter<byte> output)
    {
        if (message is not LocalMessage local)
            throw new ArgumentException($"Only the messages of Axiom.Mesh.Local can be sent, not {message?.GetType().Name ?? "null"}.", nameof(message));

        var writer = new WireWriter(output);
        writer.UInt16BigEndian(local.MessageId);
        local.WritePayload(ref writer);
        if (side != LocalSide.Host)
            return;

        if (local is Response response)
            outstanding.End(response.Correlation);
        else if (local is HelloAck ack)
            Volatile.Write(ref grantedRole, (int)ack.Role);
    }

    private DecodeResult DecodeFromClient(ushort id, ref WireReader reader, out LocalMessage? message)
    {
        message = null;
        switch (LocalMessageIds.ClassOf(id))
        {
            case MessageClass.Session:
                return id switch
                {
                    LocalMessageIds.Hello => Done(Hello.TryRead(ref reader, out Hello? hello) && reader.IsAtEnd, hello, "Hello", out message),
                    LocalMessageIds.Bye => Done(Bye.TryRead(ref reader, out Bye? bye) && reader.IsAtEnd, bye, "Bye", out message),
                    LocalMessageIds.Ping => Done(Ping.TryRead(ref reader, out Ping? ping) && reader.IsAtEnd, ping, "Ping", out message),
                    LocalMessageIds.Pong => Done(Pong.TryRead(ref reader, out Pong? pong) && reader.IsAtEnd, pong, "Pong", out message),
                    LocalMessageIds.HelloAck or LocalMessageIds.Drain => NotFromSender(id, "a client"),
                    _ => UnknownSessionMessage(id),
                };

            case MessageClass.Reply:
                if ((Role)Volatile.Read(ref grantedRole) == Role.User)
                    return DecodeResult.Invalid(ViolationCode.UnexpectedMessage, "A user client sent a Reply, which only a service may (LOCAL-SPEC.md §3.3).");
                return Done(Reply.TryRead(ref reader, out Reply? reply), reply, "Reply", out message);

            case MessageClass.Request:
                return DecodeRequest(id, ref reader, out message);

            case MessageClass.Response or MessageClass.Event or MessageClass.Callback:
                return NotFromSender(id, "a client");

            default:
                return NeverAssigned(id);
        }
    }

    private DecodeResult DecodeRequest(ushort id, ref WireReader reader, out LocalMessage? message)
    {
        message = null;
        uint correlation = reader.UInt32BigEndian();
        if (!reader.Ok)
            return DecodeResult.Invalid(ViolationCode.Malformed, "A request frame ends before its correlation (LOCAL-SPEC.md §3.4).");

        switch (outstanding.TryBegin(correlation))
        {
            case CorrelationCheck.Zero:
                return DecodeResult.Invalid(ViolationCode.Malformed, "A request has correlation 0 (LOCAL-SPEC.md §3.4).");
            case CorrelationCheck.Outstanding:
                return DecodeResult.Invalid(ViolationCode.InvalidData, $"A request reuses correlation {correlation}, which is still outstanding (LOCAL-SPEC.md §3.4).");
            case CorrelationCheck.TooMany:
                return DecodeResult.Invalid(ViolationCode.LimitExceeded, $"A 65th request is outstanding; at most {Limits.MaxOutstandingRequests} are allowed (LOCAL-SPEC.md §3.4, §12).");
        }

        if (!RequestTable.Default.TryGet(id, out RequestDescriptor? descriptor))
        {
            message = new UnknownRequest { Id = id, Correlation = correlation, Body = reader.RestView() };
            return DecodeResult.Ok;
        }

        if (!descriptor.TryRead(ref reader, out LocalRequest? request))
            return DecodeResult.Invalid(ViolationCode.Malformed, $"The body of {descriptor.Name} ({id}) is outside its bounds (LOCAL-SPEC.md §3.2, §11.3).");

        message = new RequestFrame(correlation, request);
        return DecodeResult.Ok;
    }

    private static DecodeResult DecodeFromHost(ushort id, ref WireReader reader, out LocalMessage? message)
    {
        message = null;
        switch (LocalMessageIds.ClassOf(id))
        {
            case MessageClass.Session:
                return id switch
                {
                    LocalMessageIds.HelloAck => Done(HelloAck.TryRead(ref reader, out HelloAck? ack) && reader.IsAtEnd, ack, "HelloAck", out message),
                    LocalMessageIds.Bye => Done(Bye.TryRead(ref reader, out Bye? bye) && reader.IsAtEnd, bye, "Bye", out message),
                    LocalMessageIds.Ping => Done(Ping.TryRead(ref reader, out Ping? ping) && reader.IsAtEnd, ping, "Ping", out message),
                    LocalMessageIds.Pong => Done(Pong.TryRead(ref reader, out Pong? pong) && reader.IsAtEnd, pong, "Pong", out message),
                    LocalMessageIds.Drain => Done(Drain.TryRead(ref reader, out Drain? drain) && reader.IsAtEnd, drain, "Drain", out message),
                    LocalMessageIds.Hello => NotFromSender(id, "the Host"),
                    _ => UnknownSessionMessage(id),
                };

            case MessageClass.Response:
                return Done(Response.TryRead(ref reader, out Response? response), response, "Response", out message);

            case MessageClass.Event:
                return DecodeEvent(id, ref reader, out message);

            case MessageClass.Callback:
                return DecodeCallback(id, ref reader, out message);

            case MessageClass.Request or MessageClass.Reply:
                return NotFromSender(id, "the Host");

            default:
                return NeverAssigned(id);
        }
    }

    private static DecodeResult DecodeEvent(ushort id, ref WireReader reader, out LocalMessage? message)
    {
        message = null;
        uint subscription = reader.UInt32BigEndian();
        if (!reader.Ok || subscription == 0)
            return DecodeResult.Invalid(ViolationCode.Malformed, "An event has no subscription, or subscription 0 (LOCAL-SPEC.md §3.5).");

        bool read = EventCatalog.TryRead(id, ref reader, out bool known, out LocalEvent? @event);
        if (!known)
        {
            message = new UnknownEvent { Id = id, SubscriptionId = subscription, Body = reader.RestView() };
            return DecodeResult.Ok;
        }

        if (!read || !reader.IsAtEnd)
            return DecodeResult.Invalid(ViolationCode.Malformed, $"The body of event {id} is outside its bounds (LOCAL-SPEC.md §3.2, §11.3).");
        message = new EventFrame(subscription, @event!);
        return DecodeResult.Ok;
    }

    private static DecodeResult DecodeCallback(ushort id, ref WireReader reader, out LocalMessage? message)
    {
        message = null;
        uint callback = reader.UInt32BigEndian();
        if (!reader.Ok || callback == 0)
            return DecodeResult.Invalid(ViolationCode.Malformed, "A callback has no id, or id 0 (LOCAL-SPEC.md §3.6).");

        if (id != ForwardedCall.Id)
        {
            message = new UnknownCallback { Id = id, CallbackId = callback, Body = reader.RestView() };
            return DecodeResult.Ok;
        }

        if (!ForwardedCall.TryRead(ref reader, out ForwardedCall? call) || !reader.IsAtEnd)
            return DecodeResult.Invalid(ViolationCode.Malformed, "The body of ForwardedCall is outside its bounds (LOCAL-SPEC.md §3.2, §11.3).");
        message = new Callback(callback, call);
        return DecodeResult.Ok;
    }

    private static DecodeResult Done(bool read, LocalMessage? decoded, string name, out LocalMessage? message)
    {
        message = read ? decoded : null;
        return message is not null
            ? DecodeResult.Ok
            : DecodeResult.Invalid(ViolationCode.Malformed, $"A {name} is outside its bounds (LOCAL-SPEC.md §3.2, §11.3).");
    }

    private static DecodeResult NotFromSender(ushort id, string sender) =>
        DecodeResult.Invalid(ViolationCode.UnexpectedMessage, $"Message id {id} ({LocalMessageIds.ClassOf(id)}) is not one {sender} may send (LOCAL-SPEC.md §3.3).");

    private static DecodeResult UnknownSessionMessage(ushort id) =>
        DecodeResult.Invalid(ViolationCode.Unsupported, $"Session message id {id} is unknown (LOCAL-SPEC.md §1.5, §11.3).");

    private static DecodeResult NeverAssigned(ushort id) =>
        DecodeResult.Invalid(ViolationCode.Unsupported, $"Message id {id} is reserved or never assigned (LOCAL-SPEC.md §3.3, §11.3).");
}
