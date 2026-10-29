using System;
using System.Text;
using System.Text.Unicode;
using Axiom.ProtoStream.Codecs;
using Axiom.ProtoStream.Framing;

namespace Axiom.Mesh;

/// <summary>
/// The variable-size types of SPEC.md §5.2 over ProtoStream's <see cref="WireReader"/> and <see cref="WireWriter"/>:
/// <c>string(max)</c>, <c>blob(max)</c>, <c>list&lt;T&gt;(max)</c> and <c>biglist&lt;T&gt;(max)</c>. The fixed-size ones
/// read and write themselves (<see cref="Hash32"/> and the ids, <see cref="PublicKey"/>, <see cref="Signature64"/>).
/// </summary>
/// <remarks>
/// Every reader checks the length or count against its maximum from the length field, before it reads any content
/// (§5.2), and never throws: a value outside its bounds fails the reader, and the caller checks <c>reader.Ok</c> once,
/// which makes it a violation in a message and malformed in an object. Every writer refuses a value outside its
/// bounds with an <see cref="ArgumentException"/>: what this side sends is always well-formed.
/// </remarks>
public static class Wire
{
    /// <summary>Reads a <c>string(max)</c>: a <c>u16</c> byte length, then that many bytes of valid UTF-8 without NUL.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="maxBytes">The maximum length in bytes.</param>
    /// <returns>The text, or empty when the reader failed.</returns>
    public static string ReadString(ref WireReader reader, int maxBytes)
    {
        ReadOnlyMemory<byte> bytes = ReadStringBytes(ref reader, maxBytes);
        return reader.Ok ? Encoding.UTF8.GetString(bytes.Span) : string.Empty;
    }

    /// <summary>
    /// Reads a <c>string(max)</c> as its validated UTF-8 bytes, a view of the reader's memory, for callers that compare
    /// strings as byte strings (SPEC.md §8.4, §8.7).
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <param name="maxBytes">The maximum length in bytes.</param>
    /// <returns>The bytes, or empty when the reader failed.</returns>
    public static ReadOnlyMemory<byte> ReadStringBytes(ref WireReader reader, int maxBytes)
    {
        ReadOnlyMemory<byte> bytes = reader.BytesView(LengthPrefix.UInt16BigEndian, maxBytes);
        if (!reader.Ok)
            return default;
        if (bytes.Span.Contains((byte)0) || !Utf8.IsValid(bytes.Span))
        {
            reader.Fail();
            return default;
        }

        return bytes;
    }

    /// <summary>Writes a <c>string(max)</c>.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The text: no NUL, no unpaired surrogate, at most <paramref name="maxBytes"/> UTF-8 bytes.</param>
    /// <param name="maxBytes">The maximum length in bytes.</param>
    /// <exception cref="ArgumentException">The text breaks one of those bounds.</exception>
    public static void WriteString(ref WireWriter writer, string value, int maxBytes)
    {
        int length = StringLength(value, maxBytes, nameof(value));
        writer.UInt16BigEndian((ushort)length);
        writer.Utf8(value);
    }

    /// <summary>
    /// The UTF-8 length of a text that may be written as a <c>string(max)</c>, for producers that check their values
    /// before they encode anything.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="maxBytes">The maximum length in bytes.</param>
    /// <param name="field">The name of the field, for the exception.</param>
    /// <returns>The length in bytes.</returns>
    /// <exception cref="ArgumentException">The text holds a NUL or an unpaired surrogate, or is longer than <paramref name="maxBytes"/>.</exception>
    public static int StringLength(string value, int maxBytes, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException($"The {field} holds a NUL character, which a string of SPEC.md §5.2 may not.", field);
        if (!IsWellFormedUtf16(value))
            throw new ArgumentException($"The {field} holds an unpaired surrogate, which has no UTF-8 encoding (SPEC.md §5.2).", field);

        int length = Encoding.UTF8.GetByteCount(value);
        if (length > maxBytes)
            throw new ArgumentException($"The {field} is {length} UTF-8 bytes; at most {maxBytes} are allowed (SPEC.md §5.2).", field);
        return length;
    }

    /// <summary>Reads a <c>blob(max)</c> (a <c>u32</c> length, then the bytes) as a view of the reader's memory.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="maxBytes">The maximum length.</param>
    /// <returns>The bytes, or empty when the reader failed.</returns>
    public static ReadOnlyMemory<byte> ReadBlob(ref WireReader reader, int maxBytes) =>
        reader.BytesView(LengthPrefix.UInt32BigEndian, maxBytes);

    /// <summary>Writes a <c>blob(max)</c>.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The bytes.</param>
    /// <param name="maxBytes">The maximum length.</param>
    /// <exception cref="ArgumentException">The bytes are longer than <paramref name="maxBytes"/>.</exception>
    public static void WriteBlob(ref WireWriter writer, ReadOnlySpan<byte> value, int maxBytes)
    {
        if (value.Length > maxBytes)
            throw new ArgumentException($"A blob of {value.Length} bytes exceeds its maximum of {maxBytes} (SPEC.md §5.2).", nameof(value));
        writer.UInt32BigEndian((uint)value.Length);
        writer.Bytes(value);
    }

    /// <summary>Reads the <c>u16</c> count of a <c>list&lt;T&gt;(max)</c>, before any element.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="maxCount">The maximum count.</param>
    /// <returns>The count, or 0 when the reader failed.</returns>
    public static int ReadListCount(ref WireReader reader, int maxCount) => reader.Count(LengthPrefix.UInt16BigEndian, maxCount);

    /// <summary>Reads the <c>u32</c> count of a <c>biglist&lt;T&gt;(max)</c>, before any element.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="maxCount">The maximum count.</param>
    /// <returns>The count, or 0 when the reader failed.</returns>
    public static int ReadBigListCount(ref WireReader reader, int maxCount) => reader.Count(LengthPrefix.UInt32BigEndian, maxCount);

    /// <summary>Writes the <c>u16</c> count of a <c>list&lt;T&gt;(max)</c>.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="count">The count.</param>
    /// <param name="maxCount">The maximum count.</param>
    /// <exception cref="ArgumentException">The count is negative or above <paramref name="maxCount"/>.</exception>
    public static void WriteListCount(ref WireWriter writer, int count, int maxCount) =>
        writer.UInt16BigEndian((ushort)CheckCount(count, Math.Min(maxCount, ushort.MaxValue)));

    /// <summary>Writes the <c>u32</c> count of a <c>biglist&lt;T&gt;(max)</c>.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="count">The count.</param>
    /// <param name="maxCount">The maximum count.</param>
    /// <exception cref="ArgumentException">The count is negative or above <paramref name="maxCount"/>.</exception>
    public static void WriteBigListCount(ref WireWriter writer, int count, int maxCount) =>
        writer.UInt32BigEndian((uint)CheckCount(count, maxCount));

    private static int CheckCount(int count, int maxCount)
    {
        if (count < 0 || count > maxCount)
            throw new ArgumentException($"A list of {count} elements exceeds its maximum of {maxCount} (SPEC.md §5.2).", nameof(count));
        return count;
    }

    internal static bool IsWellFormedUtf16(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                i++;
            else if (char.IsSurrogate(value[i]))
                return false;
        }

        return true;
    }
}
