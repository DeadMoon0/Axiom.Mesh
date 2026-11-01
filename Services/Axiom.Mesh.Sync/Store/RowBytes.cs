using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Axiom.Mesh.Sync;

/// <summary>Ids, hashes and keys as the database's byte columns hold them. Pure.</summary>
internal static class RowBytes
{
    /// <summary>The column value of an id or hash: its 32 bytes.</summary>
    public static byte[] Of(Hash32 value)
    {
        byte[] bytes = new byte[Hash32.Length];
        value.CopyTo(bytes);
        return bytes;
    }

    /// <summary>The id or hash a column holds; <see langword="null"/> or not 32 bytes reads as zero.</summary>
    public static Hash32 Hash(byte[]? bytes) => bytes is { Length: Hash32.Length } ? new Hash32(bytes) : default;

    /// <summary>Hashes concatenated, 32 bytes each.</summary>
    public static byte[] Join(IReadOnlyCollection<ObjectHash> hashes)
    {
        byte[] bytes = new byte[hashes.Count * Hash32.Length];
        int at = 0;
        foreach (ObjectHash hash in hashes)
        {
            hash.Value.CopyTo(bytes.AsSpan(at));
            at += Hash32.Length;
        }

        return bytes;
    }

    /// <summary>The hashes a concatenation holds.</summary>
    public static IReadOnlyList<ObjectHash> Split(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < Hash32.Length)
            return [];
        var hashes = new ObjectHash[bytes.Length / Hash32.Length];
        for (int i = 0; i < hashes.Length; i++)
            hashes[i] = new ObjectHash(new Hash32(bytes.AsSpan(i * Hash32.Length, Hash32.Length)));
        return hashes;
    }

    /// <summary>
    /// A fingerprint's or a put's key: 32 bytes that differ from row to row, then the binding's id. LDB keys a byte column by
    /// its first 8 bytes, so the part that differs comes first: led by the binding's id, every row of a binding would share one
    /// index key, and each insert would check them all.
    /// </summary>
    public static byte[] Keyed(byte[] binding, ReadOnlySpan<byte> rest)
    {
        byte[] key = new byte[rest.Length + binding.Length];
        rest.CopyTo(key);
        binding.CopyTo(key, rest.Length);
        return key;
    }

    /// <summary>A fingerprint's key: the SHA-256 of the path's UTF-8, then the binding's id.</summary>
    public static byte[] PathKey(byte[] binding, string path) => Keyed(binding, SHA256.HashData(Encoding.UTF8.GetBytes(path)));

    /// <summary>A number as an 8-byte big-endian key.</summary>
    public static byte[] Number(ulong value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }
}
