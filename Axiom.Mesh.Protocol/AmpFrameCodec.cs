using System;
using System.Buffers;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Codecs;
using Axiom.ProtoStream.Framing;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// The content of an AMP/1 frame (SPEC.md §5.1): a <c>u16</c> message id, then the body. Stateless; one instance serves
/// every session.
/// </summary>
/// <remarks>
/// A frame of fewer than 2 bytes, id 0, or an id above 255 is a violation (§5.1, §1.4, §12.1); 17 to 255 is an
/// <see cref="Extension"/>, read and discarded. A body must be read exactly to its end; a value over its bound fails
/// the read before any content is read (§5.2). A malformed <c>Bye</c> reads as a close (§6.1). ProtoStream's
/// <c>.Messages(...)</c> would turn an unknown id into a violation, which §1.4 forbids for 17 to 255 (plan D5).
/// </remarks>
internal sealed class AmpFrameCodec : IFrameCodec<MeshMessage, MeshMessage>
{
    private AmpFrameCodec()
    {
    }

    /// <summary>The one instance.</summary>
    public static AmpFrameCodec Instance { get; } = new();

    /// <inheritdoc />
    public DecodeResult Decode(in Frame frame, out MeshMessage message)
    {
        message = null!;
        if (frame.Memory.Length < Limits.MinFrameLength)
            return DecodeResult.Invalid(ViolationCode.Malformed, $"A frame of {frame.Memory.Length} bytes holds no message id (SPEC.md §5.1).");

        var reader = new WireReader(frame.Memory);
        ushort id = reader.UInt16BigEndian();
        return id switch
        {
            MessageIds.Hello => Read<Hello>(ref reader, out message),
            MessageIds.Packages => Read<Packages>(ref reader, out message),
            MessageIds.PackageChanged => Read<PackageChanged>(ref reader, out message),
            MessageIds.Have => Read<Have>(ref reader, out message),
            MessageIds.Want => Read<Want>(ref reader, out message),
            MessageIds.Object => ReadObject(ref reader, frame.Stamp, out message),
            MessageIds.NotFound => Read<NotFound>(ref reader, out message),
            MessageIds.Cancel => Read<Cancel>(ref reader, out message),
            MessageIds.RosterQuery => Read<RosterQuery>(ref reader, out message),
            MessageIds.ServiceOpen => Read<ServiceOpen>(ref reader, out message),
            MessageIds.ServiceAccepted => Read<ServiceAccepted>(ref reader, out message),
            MessageIds.ServiceRefused => Read<ServiceRefused>(ref reader, out message),
            MessageIds.Ping => Read<Ping>(ref reader, out message),
            MessageIds.Pong => Read<Pong>(ref reader, out message),
            MessageIds.Bye => ReadBye(ref reader, out message),
            MessageIds.RosterNotFound => Read<RosterNotFound>(ref reader, out message),
            >= MessageIds.FirstExtension and <= MessageIds.LastExtension => Ignore(id, out message),
            _ => DecodeResult.Invalid(ViolationCode.UnexpectedMessage, $"Message id {id} is not assigned (SPEC.md §1.4, §14.1)."),
        };
    }

    /// <inheritdoc />
    public void Encode(MeshMessage message, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(message);
        var writer = new WireWriter(output);
        writer.UInt16BigEndian(message.Id);
        message.Write(ref writer);
    }

    private static DecodeResult Read<T>(ref WireReader reader, out MeshMessage message)
        where T : MeshMessage, IWireMessage<T>
    {
        bool ok = T.TryRead(ref reader, out T read) && reader.IsAtEnd;
        message = read;
        return ok ? DecodeResult.Ok : Malformed(read.Id);
    }

    private static DecodeResult ReadObject(ref WireReader reader, MessageStamp stamp, out MeshMessage message)
    {
        bool ok = ObjectMessage.TryRead(ref reader, stamp, out ObjectMessage read) && reader.IsAtEnd;
        message = read;
        return ok
            ? DecodeResult.Ok
            : DecodeResult.Invalid(ViolationCode.InvalidData, "An Object is malformed, exceeds 4 MiB, or its hash is not H(object) (SPEC.md §7.6).");
    }

    // A malformed Bye is treated as a close (§6.1): answering it with Bye(Violation) would send a frame after the peer's Bye.
    private static DecodeResult ReadBye(ref WireReader reader, out MeshMessage message)
    {
        bool ok = Bye.TryRead(ref reader, out Bye read) && reader.IsAtEnd;
        message = ok ? read : Bye.Unreadable;
        return DecodeResult.Ok;
    }

    private static DecodeResult Ignore(ushort id, out MeshMessage message)
    {
        message = new Extension(id);
        return DecodeResult.Ok;
    }

    private static DecodeResult Malformed(ushort id) =>
        DecodeResult.Invalid(ViolationCode.Malformed, $"Message {id} is malformed or exceeds a bound (SPEC.md §5.2, §7).");
}
