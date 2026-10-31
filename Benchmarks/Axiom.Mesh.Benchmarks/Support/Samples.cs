using System;

namespace Axiom.Mesh.Benchmarks;

/// <summary>Deterministic content for the benchmarks.</summary>
internal static class Samples
{
    /// <summary><paramref name="length"/> bytes that differ per <paramref name="seed"/> and per position.</summary>
    /// <param name="length">How many.</param>
    /// <param name="seed">Makes two buffers differ.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Bytes(int length, int seed)
    {
        byte[] bytes = new byte[length];
        Stamp(bytes, seed);
        return bytes;
    }

    /// <summary>Overwrites <paramref name="bytes"/> with a pattern of <paramref name="seed"/> and the position.</summary>
    /// <param name="bytes">The buffer.</param>
    /// <param name="seed">Makes two buffers differ.</param>
    public static void Stamp(Span<byte> bytes, long seed)
    {
        for (int i = 0; i + 8 <= bytes.Length; i += 8)
            BitConverter.TryWriteBytes(bytes[i..], (seed << 32) ^ i);
        for (int i = bytes.Length & ~7; i < bytes.Length; i++)
            bytes[i] = (byte)(seed + i);
    }
}
