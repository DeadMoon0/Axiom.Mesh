using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local.Tests.Support;

/// <summary>Builds frames by hand and runs them through the two definitions' codecs.</summary>
internal static class Frames
{
    /// <summary>The frame a client sends for <paramref name="message"/>, length prefix included.</summary>
    public static byte[] FromClient(IClientMessage message)
    {
        using var runner = new CodecRunner<IHostMessage, IClientMessage>(LocalProtocol.ClientSide);
        var output = new ArrayBufferWriter<byte>();
        runner.Encode(message, output);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>The frame the Host sends for <paramref name="message"/>, length prefix included.</summary>
    public static byte[] FromHost(IHostMessage message)
    {
        using var runner = new CodecRunner<IClientMessage, IHostMessage>(LocalProtocol.HostSide);
        var output = new ArrayBufferWriter<byte>();
        runner.Encode(message, output);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>A frame with any id and payload: the length prefix, the id, the bytes.</summary>
    public static byte[] Raw(ushort id, ReadOnlySpan<byte> payload)
    {
        byte[] frame = new byte[4 + 2 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)(2 + payload.Length));
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), id);
        payload.CopyTo(frame.AsSpan(6));
        return frame;
    }

    /// <summary>The payload of a frame, everything after its id.</summary>
    public static byte[] Payload(byte[] frame) => frame[6..];

    /// <summary>The bytes a writer produces.</summary>
    public static byte[] Write(WriteAction write)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new WireWriter(output);
        write(ref writer);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>Concatenates byte arrays.</summary>
    public static byte[] Join(params IEnumerable<byte[]> parts) => [.. parts.SelectMany(p => p)];

    /// <summary>Decodes one frame on the Host side.</summary>
    public static DecodeOutcome<IClientMessage> AtHost(byte[] frame) => AtHost(new CodecRunner<IClientMessage, IHostMessage>(LocalProtocol.HostSide), frame);

    /// <summary>Decodes one frame on the Host side with a runner that keeps the session's state.</summary>
    public static DecodeOutcome<IClientMessage> AtHost(CodecRunner<IClientMessage, IHostMessage> runner, byte[] frame) =>
        runner.Decode(new ReadOnlySequence<byte>(frame), isCompleted: true);

    /// <summary>Decodes one frame on the client side.</summary>
    public static DecodeOutcome<IHostMessage> AtClient(byte[] frame)
    {
        using var runner = new CodecRunner<IHostMessage, IClientMessage>(LocalProtocol.ClientSide);
        return runner.Decode(new ReadOnlySequence<byte>(frame), isCompleted: true);
    }

    /// <summary>Writes something with a <see cref="WireWriter"/>.</summary>
    public delegate void WriteAction(ref WireWriter writer);
}
