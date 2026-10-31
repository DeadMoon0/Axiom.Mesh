using System;
using System.Buffers;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Payloads;

/// <summary>Encodes a payload in the layouts of PAYLOADS.md, the inverse of <see cref="EntryPayload.TryParse"/>.</summary>
public static class PayloadEncoder
{
    /// <summary>The payload's bytes.</summary>
    /// <param name="payload">The payload.</param>
    /// <returns>The encoding, at most 1 MiB.</returns>
    /// <exception cref="ArgumentException">A field is outside its bounds, or the encoding exceeds 1 MiB.</exception>
    public static byte[] Encode(EntryPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var output = new ArrayBufferWriter<byte>();
        var writer = new WireWriter(output);
        switch (payload)
        {
            case VersionPayload version:
                version.Write(ref writer);
                break;
            case MessagePayload message:
                message.Write(ref writer);
                break;
            case RedactionPayload redaction:
                redaction.Write(ref writer);
                break;
            case OpaquePayload opaque:
                writer.Bytes(opaque.Bytes.Span);
                break;
        }

        if (output.WrittenCount > Limits.MaxPayloadLength)
            throw new ArgumentException($"The payload is {output.WrittenCount} bytes; at most {Limits.MaxPayloadLength} are allowed (SPEC.md §8.6).", nameof(payload));
        return output.WrittenSpan.ToArray();
    }
}
