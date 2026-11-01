using System;

namespace Axiom.Mesh.Sync;

/// <summary>
/// When a burst of changes in a bound directory starts a run (SYNC-SERVICE.md §3): once the directory was quiet for
/// <see cref="SyncLimits.DebounceQuiet"/>, and never later than <see cref="SyncLimits.DebounceCap"/> after the burst's first
/// change, however busy the directory stays. Pure.
/// </summary>
internal static class Debounce
{
    /// <summary>When the run is due.</summary>
    /// <param name="firstChange">The first change of the burst.</param>
    /// <param name="lastChange">The latest change.</param>
    /// <returns>The time the run starts.</returns>
    public static DateTimeOffset Next(DateTimeOffset firstChange, DateTimeOffset lastChange)
    {
        DateTimeOffset quiet = lastChange + SyncLimits.DebounceQuiet;
        DateTimeOffset cap = firstChange + SyncLimits.DebounceCap;
        return quiet < cap ? quiet : cap;
    }
}
