using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Axiom.Mesh.Protocol.Tests.Support;

/// <summary>
/// Message bodies written by hand, field by field, with no bound checked: what a peer that breaks the rules sends.
/// </summary>
internal sealed class Bytes
{
    private readonly List<byte> bytes = [];

    /// <summary>A <c>u8</c>.</summary>
    public Bytes U8(byte value)
    {
        bytes.Add(value);
        return this;
    }

    /// <summary>A <c>u16</c>, big-endian.</summary>
    public Bytes U16(int value)
    {
        Span<byte> span = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)value);
        return Raw(span);
    }

    /// <summary>A <c>u32</c>, big-endian.</summary>
    public Bytes U32(uint value)
    {
        Span<byte> span = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        return Raw(span);
    }

    /// <summary>A <c>u64</c>, big-endian.</summary>
    public Bytes U64(ulong value)
    {
        Span<byte> span = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(span, value);
        return Raw(span);
    }

    /// <summary><paramref name="count"/> bytes of <paramref name="fill"/>.</summary>
    public Bytes Fill(int count, byte fill)
    {
        for (int i = 0; i < count; i++)
            bytes.Add(fill);
        return this;
    }

    /// <summary>A <c>string</c>: a <c>u16</c> length and the UTF-8 bytes, whatever their length.</summary>
    public Bytes Text(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        return U16(utf8.Length).Raw(utf8);
    }

    /// <summary>A <c>list&lt;hash&gt;</c> of <paramref name="count"/> ascending hashes, whatever the count.</summary>
    public Bytes HashList(int count)
    {
        U16(count);
        Span<byte> raw = stackalloc byte[Hash32.Length];
        foreach (ObjectHash hash in MessageSamples.Hashes(count))
        {
            hash.Value.CopyTo(raw);
            Raw(raw);
        }

        return this;
    }

    /// <summary>Bytes as they are.</summary>
    public Bytes Raw(ReadOnlySpan<byte> value)
    {
        foreach (byte b in value)
            bytes.Add(b);
        return this;
    }

    /// <summary>The body written so far.</summary>
    public byte[] ToArray() => [.. bytes];
}
