using System;
using System.Threading;

namespace Axiom.Mesh.Discovery;

/// <summary>The check every shell applies to its configured interval.</summary>
internal static class Intervals
{
    /// <summary>Refuses an interval that is neither positive nor <see cref="Timeout.InfiniteTimeSpan"/>.</summary>
    /// <param name="interval">The configured interval.</param>
    /// <param name="name">The option's name, for the message.</param>
    /// <exception cref="ArgumentOutOfRangeException">The interval is zero or negative but not infinite.</exception>
    public static void Check(TimeSpan interval, string name)
    {
        if (interval <= TimeSpan.Zero && interval != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(name, interval, "An interval is positive, or Timeout.InfiniteTimeSpan for never.");
    }
}
