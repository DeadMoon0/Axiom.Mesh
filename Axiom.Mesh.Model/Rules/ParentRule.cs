using System;

namespace Axiom.Mesh;

/// <summary>
/// The clock and parents of the next Entry a device writes to a Package when the writer names none (SPEC.md §9.3;
/// LOCAL-SPEC.md §9.4, decision 6).
/// </summary>
public static class ParentRule
{
    /// <summary>
    /// Chooses the clock and the parents:
    /// <list type="bullet">
    /// <item>nothing held: clock 0, no parents;</item>
    /// <item>Entries held but no own Head: the Entry highest in the total order;</item>
    /// <item>an own Head within 2^32 of the new clock: the own Head;</item>
    /// <item>an own Head further below: the own Head and the Entry highest in the total order, whose clock is one below
    /// the new clock, so the Entry stays faultless (§8.6);</item>
    /// <item>a new clock of 2^63 or more: <see cref="ParentChoice.ClockExhausted"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="ownHead">The writing device's Head of the Package, or <see langword="null"/>.</param>
    /// <param name="highest">The Entry of the Package highest in the total order, or <see langword="null"/> when none is held.</param>
    /// <returns>The choice.</returns>
    /// <exception cref="ArgumentException">An own Head is given but nothing is held, or the Head sorts above the highest Entry.</exception>
    public static ParentChoice Choose(EntryKey? ownHead, EntryKey? highest)
    {
        if (highest is not { } top)
        {
            if (ownHead is not null)
                throw new ArgumentException("A device that holds no Entry of the Package has no Head of it.", nameof(ownHead));
            return new ParentChoice.Write(0, EntryParents.None);
        }

        if (ownHead is { } own && own > top)
            throw new ArgumentException("The own Head sorts above the Entry given as highest in the total order.", nameof(ownHead));

        ulong clock = top.Clock + 1;
        if (clock >= Limits.ClockLimit)
            return new ParentChoice.ClockExhausted();

        if (ownHead is not { } head)
            return new ParentChoice.Write(clock, EntryParents.Of(top.Hash));

        return clock - head.Clock <= Limits.MaxClockGap
            ? new ParentChoice.Write(clock, EntryParents.Of(head.Hash))
            : new ParentChoice.Write(clock, EntryParents.Of(head.Hash, top.Hash));
    }
}
