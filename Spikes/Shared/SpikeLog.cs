using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Spikes;

/// <summary>Lines the spike writes, kept so the scenario can check them, and forwarded to a sink as they arrive.</summary>
public sealed class SpikeLog
{
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly Action<string> _sink;

    public SpikeLog(Action<string> sink)
    {
        _sink = sink;
    }

    public IReadOnlyList<string> Lines => _lines.ToArray();

    public void Add(string line)
    {
        _lines.Enqueue(line);
        _sink(line);
    }

    public bool Contains(string fragment) => _lines.Any(l => l.Contains(fragment, StringComparison.Ordinal));

    /// <summary>Waits until every fragment appears, or the timeout passes; returns whether they all appeared.</summary>
    public async Task<bool> WaitForAsync(TimeSpan timeout, params string[] fragments)
    {
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < timeout)
        {
            if (fragments.All(Contains))
                return true;
            await Task.Delay(25).ConfigureAwait(false);
        }

        return fragments.All(Contains);
    }
}
