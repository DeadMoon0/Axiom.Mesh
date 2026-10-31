using System;
using System.Collections.Generic;

namespace Axiom.Mesh;

/// <summary>The clock rules of an Entry (SPEC.md §8.6, §9.3).</summary>
public static class Lamport
{
    /// <summary>
    /// Whether a clock and its parents are well-formed: the clock is below 2^63, and the parents are empty only at
    /// clock 0 (SPEC.md §8.6). An Entry that breaks this is malformed.
    /// </summary>
    /// <param name="clock">The Entry's clock.</param>
    /// <param name="parents">The Entry's parents.</param>
    /// <returns><see langword="true"/> when well-formed.</returns>
    public static bool IsWellFormed(ulong clock, EntryParents parents) =>
        clock < Limits.ClockLimit && (clock == 0 || !parents.IsEmpty);

    /// <summary>
    /// Finds what makes an Entry faulty once its parents are held (SPEC.md §8.6, §12.2): a parent of another Package, a
    /// clock not greater than every parent's, or a clock more than 2^32 above the greatest parent's (above 2^32 with no
    /// parents).
    /// </summary>
    /// <param name="entry">The Entry.</param>
    /// <param name="parents">The Entry's parents as held, one for each hash in <c>entry.Parents</c>, in any order.</param>
    /// <param name="fault">The fault, when there is one.</param>
    /// <returns><see langword="true"/> when the Entry is faulty.</returns>
    /// <exception cref="ArgumentException"><paramref name="parents"/> are not the Entry's parents.</exception>
    public static bool TryFindParentFault(EntryContent entry, IReadOnlyList<EntryFacts> parents, out ObjectFault fault)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return TryFindParentFault(entry.Facts, entry.Parents, parents, out fault);
    }

    /// <summary>
    /// The same rule for an Entry known by its facts and the parents it names, as an index keeps it: an Entry stored before
    /// its parents is checked when the last of them is stored (SPEC.md §8.6), without reading the Entry again.
    /// </summary>
    /// <param name="entry">The Entry's facts.</param>
    /// <param name="entryParents">The parents the Entry names.</param>
    /// <param name="parents">The Entry's parents as held, one for each of <paramref name="entryParents"/>, in any order.</param>
    /// <param name="fault">The fault, when there is one.</param>
    /// <returns><see langword="true"/> when the Entry is faulty.</returns>
    /// <exception cref="ArgumentException"><paramref name="parents"/> are not the Entry's parents.</exception>
    public static bool TryFindParentFault(EntryFacts entry, EntryParents entryParents, IReadOnlyList<EntryFacts> parents, out ObjectFault fault)
    {
        ArgumentNullException.ThrowIfNull(parents);
        if (parents.Count != entryParents.Count)
            throw new ArgumentException($"The Entry names {entryParents.Count} parents; {parents.Count} were given.", nameof(parents));

        ulong highest = 0;
        foreach (EntryFacts parent in parents)
        {
            if (!entryParents.Contains(parent.Key.Hash))
                throw new ArgumentException($"Entry {parent.Key.Hash} is not a parent of the Entry.", nameof(parents));
            if (parent.Package != entry.Package)
                return Faulty("An Entry names a parent of another Package (SPEC.md §8.6).", out fault);
            if (entry.Key.Clock <= parent.Key.Clock)
                return Faulty("An Entry's clock is not greater than a parent's (SPEC.md §8.6).", out fault);
            highest = Math.Max(highest, parent.Key.Clock);
        }

        if (entry.Key.Clock - highest > Limits.MaxClockGap)
            return Faulty("An Entry's clock is more than 2^32 above its greatest parent's (SPEC.md §8.6).", out fault);

        fault = default;
        return false;
    }

    private static bool Faulty(string detail, out ObjectFault fault)
    {
        fault = ObjectFault.Faulty(detail);
        return true;
    }
}
