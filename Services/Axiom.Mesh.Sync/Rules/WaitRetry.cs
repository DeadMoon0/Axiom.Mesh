using System;

namespace Axiom.Mesh.Sync;

/// <summary>
/// When a binding that waits for objects no peer supplied yet tries again (SYNC-SERVICE.md §3 step 9): after
/// <see cref="First"/>, doubling with every wait in a row, never longer than <see cref="Longest"/>. Nothing tells the service
/// that a peer came to hold the objects, so it asks again. Pure.
/// </summary>
internal static class WaitRetry
{
    /// <summary>The first wait.</summary>
    public static readonly TimeSpan First = TimeSpan.FromSeconds(2);

    /// <summary>The longest wait.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromMinutes(1);

    /// <summary>How long to wait before the next try.</summary>
    /// <param name="waitsInARow">How many runs in a row ended waiting for objects, this one included (1 or more).</param>
    /// <returns>The wait.</returns>
    public static TimeSpan After(int waitsInARow)
    {
        int doublings = Math.Clamp(waitsInARow - 1, 0, 16);
        TimeSpan wait = First * (1L << doublings);
        return wait < Longest ? wait : Longest;
    }
}
