using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// The announcement rate rule of SPEC.md §3.2: a node MUST NOT send more than one announcement per second per
/// interface. Every announcement a shell sends, an answer to a query included, passes this gate first, so a flood of
/// queries costs at most one answer per second per interface. Takes timestamps, holds no clock.
/// </summary>
/// <remarks>Thread-safe: a shell's receive loops and its announcer share one gate.</remarks>
public sealed class AnnounceGate
{
    /// <summary>The least time between two announcements on one interface (SPEC.md §3.2).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    // Keyed by interface index, which the operating system assigns; a host has a handful, so the map stays small.
    private readonly Dictionary<int, TimeSpan> lastPassed = [];
    private readonly object sync = new();

    /// <summary>
    /// Whether an announcement on <paramref name="interfaceIndex"/> may go out at <paramref name="timestamp"/>: the first
    /// one always, later ones once a full second has passed since the last that passed. A refused announcement does not
    /// count.
    /// </summary>
    /// <param name="timestamp">Monotonic time, for instance <see cref="TimeProvider.GetElapsedTime(long)"/> since a fixed start.</param>
    /// <param name="interfaceIndex">The operating system's index of the interface the announcement leaves on.</param>
    /// <returns><see langword="true"/> when the announcement may be sent; the gate then counts it as sent.</returns>
    public bool TryPass(TimeSpan timestamp, int interfaceIndex)
    {
        lock (sync)
        {
            // A timestamp taken before the last pass (two threads racing) is refused too: it is less than a second later.
            if (lastPassed.TryGetValue(interfaceIndex, out TimeSpan last) && timestamp - last < Interval)
                return false;

            lastPassed[interfaceIndex] = timestamp;
            return true;
        }
    }
}
