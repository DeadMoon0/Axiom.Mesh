using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// 32 bytes held inline: a SHA-256 output (SPEC.md §2.1), or another 32-byte value such as a nonce or one half of a
/// signature. Equal when the bytes are equal; ordered as unsigned big-endian byte strings, which is the order SPEC.md
/// uses everywhere (§4.5, §7.2, §8, §9.3).
/// </summary>
/// <remarks>
/// The bytes are kept as four big-endian words, so comparing, hashing and copying never touch the heap and a
/// comparison is at most four word compares. <c>default</c> is the all-zero value.
/// </remarks>
[ClosedShape("SHA-256 has 32 bytes of output for ever (SPEC.md §2.1).")]
public readonly record struct Hash32 : IComparable<Hash32>
{
    /// <summary>The number of bytes, 32.</summary>
    public const int Length = 32;

    private readonly ulong w0;
    private readonly ulong w1;
    private readonly ulong w2;
    private readonly ulong w3;

    /// <summary>Copies exactly 32 bytes.</summary>
    /// <param name="bytes">The 32 bytes, most significant first.</param>
    /// <exception cref="ArgumentException">The span is not 32 bytes long.</exception>
    public Hash32(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
            throw new ArgumentException($"A Hash32 is exactly {Length} bytes; {bytes.Length} were given.", nameof(bytes));

        w0 = BinaryPrimitives.ReadUInt64BigEndian(bytes);
        w1 = BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
        w2 = BinaryPrimitives.ReadUInt64BigEndian(bytes[16..]);
        w3 = BinaryPrimitives.ReadUInt64BigEndian(bytes[24..]);
    }

    internal Hash32(ulong w0, ulong w1, ulong w2, ulong w3)
    {
        this.w0 = w0;
        this.w1 = w1;
        this.w2 = w2;
        this.w3 = w3;
    }

    /// <summary>Whether every byte is zero, the value SPEC.md uses for "none" (§8.5 <c>replyTo</c>, §8.7 branch heads).</summary>
    public bool IsZero => (w0 | w1 | w2 | w3) == 0;

    internal ulong W0 => w0;

    internal ulong W1 => w1;

    internal ulong W2 => w2;

    internal ulong W3 => w3;

    /// <summary><c>H(data)</c>: SHA-256 of the bytes (SPEC.md §2.1).</summary>
    /// <param name="data">The bytes to hash.</param>
    /// <returns>The hash.</returns>
    public static Hash32 Of(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[Length];
        SHA256.HashData(data, hash);
        return new Hash32(hash);
    }

    /// <summary>Reads 32 bytes. On a short frame the reader fails and the result is zero; check <c>reader.Ok</c>.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The value read.</returns>
    public static Hash32 Read(ref WireReader reader)
    {
        ReadOnlyMemory<byte> bytes = reader.BytesView(Length);
        return reader.Ok ? new Hash32(bytes.Span) : default;
    }

    /// <summary>Parses 64 hex digits, either case.</summary>
    /// <param name="hex">The text.</param>
    /// <param name="value">The value, or zero when the text is not 64 hex digits.</param>
    /// <returns><see langword="true"/> when the text was 64 hex digits.</returns>
    public static bool TryParseHex(ReadOnlySpan<char> hex, out Hash32 value)
    {
        Span<byte> bytes = stackalloc byte[Length];
        if (!Hex.TryDecode(hex, bytes))
        {
            value = default;
            return false;
        }

        value = new Hash32(bytes);
        return true;
    }

    /// <summary>Writes the 32 bytes.</summary>
    /// <param name="writer">The writer.</param>
    public void Write(ref WireWriter writer)
    {
        Span<byte> bytes = stackalloc byte[Length];
        CopyTo(bytes);
        writer.Bytes(bytes);
    }

    /// <summary>Copies the 32 bytes to the start of <paramref name="destination"/>.</summary>
    /// <param name="destination">At least 32 bytes.</param>
    public void CopyTo(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64BigEndian(destination, w0);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..], w1);
        BinaryPrimitives.WriteUInt64BigEndian(destination[16..], w2);
        BinaryPrimitives.WriteUInt64BigEndian(destination[24..], w3);
    }

    /// <summary>The 32 bytes as 64 lowercase hex digits (SPEC.md §1.3).</summary>
    /// <returns>The hex text.</returns>
    public string ToHex()
    {
        Span<byte> bytes = stackalloc byte[Length];
        CopyTo(bytes);
        return Hex.Lower(bytes);
    }

    /// <summary>Compares as unsigned big-endian byte strings.</summary>
    /// <param name="other">The other value.</param>
    /// <returns>Negative, zero or positive.</returns>
    public int CompareTo(Hash32 other)
    {
        if (w0 != other.w0)
            return w0 < other.w0 ? -1 : 1;
        if (w1 != other.w1)
            return w1 < other.w1 ? -1 : 1;
        if (w2 != other.w2)
            return w2 < other.w2 ? -1 : 1;
        if (w3 != other.w3)
            return w3 < other.w3 ? -1 : 1;
        return 0;
    }

    /// <inheritdoc />
    public override string ToString() => ToHex();

    /// <summary>Whether <paramref name="left"/> sorts before <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <(Hash32 left, Hash32 right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> sorts after <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >(Hash32 left, Hash32 right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> sorts before or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <=(Hash32 left, Hash32 right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> sorts after or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >=(Hash32 left, Hash32 right) => left.CompareTo(right) >= 0;
}
