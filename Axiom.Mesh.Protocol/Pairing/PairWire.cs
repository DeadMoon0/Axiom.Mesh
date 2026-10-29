using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>The field of the pairing protocol AMP/1 does not have: a user key that may be absent (PAIR-SPEC.md §5.2).</summary>
internal static class PairWire
{
    /// <summary>Reads 65 bytes: all zero is no key, else a <c>pubkey</c> on P-256; anything else fails the reader.</summary>
    public static PublicKey? ReadOptionalKey(ref WireReader reader)
    {
        ReadOnlyMemory<byte> bytes = reader.BytesView(PublicKey.Length);
        if (!reader.Ok || !bytes.Span.ContainsAnyExcept((byte)0))
            return null;
        if (PublicKey.TryParse(bytes.Span, out PublicKey? key))
            return key;
        reader.Fail();
        return null;
    }

    /// <summary>Writes a key, or 65 zero bytes for none.</summary>
    public static void WriteOptionalKey(ref WireWriter writer, PublicKey? key)
    {
        if (key is not null)
            key.Write(ref writer);
        else
            writer.Bytes(stackalloc byte[PublicKey.Length]);
    }
}
