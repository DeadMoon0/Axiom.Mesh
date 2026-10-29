using System;
using System.Buffers;
using System.Buffers.Binary;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol.Tests.Support;

/// <summary>Frames of a definition as bytes: encoded by the definition, built by hand, and decoded one at a time.</summary>
internal static class Frames
{
    /// <summary>One message as the frame the definition writes.</summary>
    public static byte[] Encode<TIn, TOut>(ProtocolDefinition<TIn, TOut> definition, TOut message)
        where TIn : class
        where TOut : class
    {
        var output = new ArrayBufferWriter<byte>();
        using var runner = new CodecRunner<TIn, TOut>(definition);
        runner.Encode(message, output);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>An AMP/1 message as its frame.</summary>
    public static byte[] Encode(MeshMessage message) => Encode(Amp.Definition, message);

    /// <summary>A frame built by hand: a <c>u32</c> length of <c>2 + body</c>, the <c>u16</c> id, the body.</summary>
    public static byte[] Raw(ushort id, ReadOnlySpan<byte> body)
    {
        byte[] frame = new byte[4 + 2 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)(2 + body.Length));
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), id);
        body.CopyTo(frame.AsSpan(6));
        return frame;
    }

    /// <summary>A frame whose length field says <paramref name="length"/>, followed by that many bytes of content.</summary>
    public static byte[] WithLength(uint length, ReadOnlySpan<byte> content)
    {
        byte[] frame = new byte[4 + content.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, length);
        content.CopyTo(frame.AsSpan(4));
        return frame;
    }

    /// <summary>The body of a message: what it writes after its id.</summary>
    public static byte[] Body(MeshMessage message) => Encode(message)[6..];

    /// <summary>Decodes the first frame of <paramref name="bytes"/> with a fresh codec of the definition.</summary>
    public static DecodeOutcome<TIn> Decode<TIn, TOut>(ProtocolDefinition<TIn, TOut> definition, byte[] bytes)
        where TIn : class
        where TOut : class
    {
        using var runner = new CodecRunner<TIn, TOut>(definition);
        return runner.Decode(new ReadOnlySequence<byte>(bytes), isCompleted: true);
    }

    /// <summary>Decodes the first AMP/1 frame of <paramref name="bytes"/>.</summary>
    public static DecodeOutcome<MeshMessage> Decode(byte[] bytes) => Decode(Amp.Definition, bytes);
}
