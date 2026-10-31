using System;
using System.Threading.Tasks;

namespace Axiom.Mesh.Tests.Shared;

/// <summary>
/// Measures what a piece of code allocates on the calling thread, robustly against a loaded machine: the code runs once
/// to warm up (tiered compilation, static caches, pools), then several times, and the least any run allocated is the
/// answer.
/// </summary>
/// <remarks>
/// What the code under test allocates, it allocates on every run, so the minimum keeps the budget exactly as strict. What a
/// busy machine adds only now and then (an <c>ArrayPool</c> rent that finds its per-core stack emptied by another thread, a
/// cache filled late because tier-1 code arrived between two runs) shows up in some runs and not in others, and the
/// minimum leaves it out. Measuring once let the whole solution's parallel run fail a budget that holds.
/// </remarks>
internal static class Allocations
{
    /// <summary>How often the code runs after the warm-up.</summary>
    public const int Runs = 7;

    /// <summary>The least the action allocated on this thread in <see cref="Runs"/> runs after one warm-up run.</summary>
    /// <param name="action">The code under test.</param>
    /// <returns>Bytes.</returns>
    public static long Least(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
        long least = long.MaxValue;
        for (int run = 0; run < Runs; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return least;
    }

    /// <summary>The least of <see cref="Runs"/> measurements after one warm-up, for code that measures itself.</summary>
    /// <param name="measured">Runs the code once and returns the bytes it allocated.</param>
    /// <returns>Bytes.</returns>
    public static long Least(Func<long> measured)
    {
        ArgumentNullException.ThrowIfNull(measured);
        measured();
        long least = long.MaxValue;
        for (int run = 0; run < Runs; run++)
            least = Math.Min(least, measured());
        return least;
    }

    /// <summary>The least of <see cref="Runs"/> measurements after one warm-up, for asynchronous code that measures itself.</summary>
    /// <param name="measured">Runs the code once and returns the bytes it allocated.</param>
    /// <returns>Bytes.</returns>
    public static async Task<long> LeastAsync(Func<Task<long>> measured)
    {
        ArgumentNullException.ThrowIfNull(measured);
        await measured().ConfigureAwait(false);
        long least = long.MaxValue;
        for (int run = 0; run < Runs; run++)
            least = Math.Min(least, await measured().ConfigureAwait(false));
        return least;
    }
}
