using System;

namespace Axiom.Mesh;

/// <summary>
/// Which stored Entry is a device's Head: per Package and writing device, the one with the highest clock, ties broken
/// by the lower hash (SPEC.md §9.3).
/// </summary>
public static class HeadRule
{
    /// <summary>Whether <paramref name="candidate"/> replaces <paramref name="current"/> as the device's Head.</summary>
    /// <param name="candidate">A newly stored Entry of the device.</param>
    /// <param name="current">The device's Head so far.</param>
    /// <returns><see langword="true"/> for a higher clock, or an equal one with a lower hash; never for the same Entry.</returns>
    /// <exception cref="ArgumentException">The two are different devices' Entries.</exception>
    public static bool Supersedes(Head candidate, Head current)
    {
        if (candidate.Device != current.Device)
            throw new ArgumentException($"A Head is per device; {candidate.Device} and {current.Device} are two devices.", nameof(candidate));
        return candidate.Clock > current.Clock || (candidate.Clock == current.Clock && candidate.Entry < current.Entry);
    }
}
