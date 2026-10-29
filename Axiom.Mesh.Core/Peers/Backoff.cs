using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// How long a node holds back from a peer (SPEC.md §4.4, §6.2, §12.1): after a failed dial or a refusal 10 seconds,
/// doubling with every further failure up to 5 minutes; after three violations within an hour, one hour.
/// </summary>
internal static class Backoff
{
    /// <summary>The wait before the next dial after <paramref name="failures"/> failures in a row.</summary>
    /// <param name="failures">Consecutive failed dials or refusals, at least 1.</param>
    /// <returns>10 seconds for the first, doubling up to 5 minutes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="failures"/> is below 1.</exception>
    public static TimeSpan Next(int failures)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failures, 1);
        long seconds = (long)Limits.RedialFirstSeconds << Math.Min(failures - 1, 16);
        return TimeSpan.FromSeconds(Math.Min(seconds, Limits.RedialMaxSeconds));
    }

    /// <summary>The wait a device's violations impose: one hour once it caused three within the window (SPEC.md §12.1).</summary>
    /// <param name="violations">The device's violations within the current window.</param>
    /// <returns>One hour from the third violation on; zero before.</returns>
    public static TimeSpan ForViolations(int violations) =>
        violations >= Limits.ViolationsPerWindow ? TimeSpan.FromSeconds(Limits.ViolationWindowSeconds) : TimeSpan.Zero;
}
