using System;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// The byte forms of ids and keys in the index. LDB 0.1.0 identifies a row by one key column, so a key of several parts
/// is their concatenation, in the order the row's documentation states; byte-string order of the concatenation is the
/// order of the parts.
/// </summary>
internal static class RowKeys
{
    /// <summary>The 32 bytes of a value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A new array.</returns>
    public static byte[] Of(Hash32 value)
    {
        byte[] bytes = new byte[Hash32.Length];
        value.CopyTo(bytes);
        return bytes;
    }

    /// <summary>Two values, one after the other.</summary>
    /// <param name="first">The first part.</param>
    /// <param name="second">The second part.</param>
    /// <returns>A new 64-byte array.</returns>
    public static byte[] Of(Hash32 first, Hash32 second)
    {
        byte[] bytes = new byte[2 * Hash32.Length];
        first.CopyTo(bytes);
        second.CopyTo(bytes.AsSpan(Hash32.Length));
        return bytes;
    }

    /// <summary>A pin's key: the object, the reason, the source (LOCAL-SPEC.md §5.11 orders pins so).</summary>
    /// <param name="hash">The pinned object.</param>
    /// <param name="reason">The reason.</param>
    /// <param name="source">The source.</param>
    /// <returns>A new 65-byte array.</returns>
    public static byte[] Pin(ObjectHash hash, PinReason reason, Hash32 source)
    {
        byte[] bytes = new byte[(2 * Hash32.Length) + 1];
        hash.Value.CopyTo(bytes);
        bytes[Hash32.Length] = (byte)reason;
        source.CopyTo(bytes.AsSpan(Hash32.Length + 1));
        return bytes;
    }

    /// <summary>Reads 32 bytes back.</summary>
    /// <param name="bytes">Exactly 32 bytes.</param>
    /// <returns>The value.</returns>
    public static Hash32 Hash(byte[] bytes) => new(bytes);

    /// <summary>Reads 32 bytes back, or zero for <see langword="null"/>.</summary>
    /// <param name="bytes">Exactly 32 bytes, or <see langword="null"/>.</param>
    /// <returns>The value.</returns>
    public static Hash32 HashOrZero(byte[]? bytes) => bytes is null ? default : new Hash32(bytes);
}
