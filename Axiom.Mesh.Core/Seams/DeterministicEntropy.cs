using System;
using System.Buffers.Binary;

namespace Axiom.Mesh.Core;

/// <summary>
/// Reproducible bytes from a seed (SplitMix64), for tests and in-memory meshes whose ids must come out the same on every
/// run. Never for keys or tokens of a real node: anyone who knows the seed knows every byte. Thread-safe.
/// </summary>
public sealed class DeterministicEntropy : IEntropy
{
    private readonly object gate = new();
    private ulong state;

    /// <summary>Starts the sequence at <paramref name="seed"/>; two instances with one seed produce the same bytes.</summary>
    /// <param name="seed">The seed.</param>
    public DeterministicEntropy(ulong seed) => state = seed;

    /// <inheritdoc />
    public void Fill(Span<byte> destination)
    {
        Span<byte> word = stackalloc byte[sizeof(ulong)];
        lock (gate)
        {
            for (int offset = 0; offset < destination.Length; offset += sizeof(ulong))
            {
                BinaryPrimitives.WriteUInt64BigEndian(word, Next());
                word[..Math.Min(sizeof(ulong), destination.Length - offset)].CopyTo(destination[offset..]);
            }
        }
    }

    private ulong Next()
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
