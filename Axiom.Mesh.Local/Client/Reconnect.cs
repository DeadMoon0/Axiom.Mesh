using System;

namespace Axiom.Mesh.Local;

/// <summary>
/// How long a client waits before it reconnects (LOCAL-SPEC.md §2.3): 1 second after the first failure, doubling up to
/// 30 seconds.
/// </summary>
public static class Reconnect
{
    /// <summary>The wait after the first failure, in seconds.</summary>
    public const int FirstDelaySeconds = 1;

    /// <summary>The longest wait, in seconds.</summary>
    public const int MaxDelaySeconds = 30;

    /// <summary>The wait before reconnect attempt <paramref name="attempt"/>.</summary>
    /// <param name="attempt">1 after the first failure, 2 after the second, and so on.</param>
    /// <returns>1 s, 2 s, 4 s, 8 s, 16 s, then 30 s for every later attempt.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="attempt"/> is below 1.</exception>
    public static TimeSpan Delay(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        // 2^(attempt-1) seconds; from the sixth attempt on the doubling has passed the cap, and a shift would overflow.
        long seconds = attempt > 5 ? MaxDelaySeconds : Math.Min((long)FirstDelaySeconds << (attempt - 1), MaxDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
