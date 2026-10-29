using System;
using System.Collections.Generic;

namespace Axiom.Mesh;

/// <summary>The order SPEC.md sorts names in: as unsigned byte strings (§8.4, §8.7).</summary>
internal sealed class ByteStrings : IComparer<byte[]>
{
    private ByteStrings()
    {
    }

    /// <summary>The comparer.</summary>
    public static ByteStrings Comparer { get; } = new();

    /// <inheritdoc />
    public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
}
