using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// The order of pins (LOCAL-SPEC.md §5.11): by <c>(hash, reason, source)</c> ascending as byte strings, which is the order
/// of their 65-byte keys. LDB sorts a byte-string column in memory (STRUCTURAL-DEBT.md 39), so a page reads the matching
/// rows and orders them here.
/// </summary>
internal static class PinOrder
{
    /// <summary>The rows after a cursor, in order.</summary>
    /// <param name="rows">The pins, any order.</param>
    /// <param name="after">The cursor's key, or <see langword="null"/> from the start.</param>
    /// <returns>The rows whose key is above the cursor, ascending.</returns>
    public static IEnumerable<PinRow> After(IEnumerable<PinRow> rows, byte[]? after) =>
        rows.Where(r => after is null || r.Key.AsSpan().SequenceCompareTo(after) > 0)
            .OrderBy(r => r.Key, KeyOrder.Instance);

    private sealed class KeyOrder : IComparer<byte[]>
    {
        public static KeyOrder Instance { get; } = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
