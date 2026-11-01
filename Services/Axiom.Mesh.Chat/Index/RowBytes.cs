using System;

namespace Axiom.Mesh.Chat;

/// <summary>Ids and hashes as the index's byte columns hold them: their 32 bytes. Pure.</summary>
internal static class RowBytes
{
    /// <summary>The column value of an id or hash.</summary>
    /// <param name="value">The id or hash.</param>
    /// <returns>Its 32 bytes.</returns>
    public static byte[] Of(Hash32 value)
    {
        byte[] bytes = new byte[Hash32.Length];
        value.CopyTo(bytes);
        return bytes;
    }

    /// <summary>The id or hash a column holds.</summary>
    /// <param name="bytes">The column value; <see langword="null"/> or not 32 bytes reads as zero.</param>
    /// <returns>The value.</returns>
    public static Hash32 Hash(byte[]? bytes) => bytes is { Length: Hash32.Length } ? new Hash32(bytes) : default;

    /// <summary>Two hashes concatenated, for a two-part key.</summary>
    /// <param name="first">The first.</param>
    /// <param name="second">The second.</param>
    /// <returns>64 bytes.</returns>
    public static byte[] Pair(Hash32 first, Hash32 second)
    {
        byte[] bytes = new byte[2 * Hash32.Length];
        first.CopyTo(bytes);
        second.CopyTo(bytes.AsSpan(Hash32.Length));
        return bytes;
    }
}
