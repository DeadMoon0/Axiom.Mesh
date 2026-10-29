using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>
/// Waits for a state that no fact announces (a heartbeat's bytes arriving): yields to the thread pool until it holds,
/// within the harness's real-time deadline. Never a sleep; time on the fake clock does not move meanwhile.
/// </summary>
internal static class Waits
{
    public static async Task UntilAsync(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TestMesh.Deadline)
                throw new TimeoutException($"Waited {TestMesh.Deadline.TotalSeconds:0} s for {what}, in vain.");
            await Task.Yield();
        }
    }
}
