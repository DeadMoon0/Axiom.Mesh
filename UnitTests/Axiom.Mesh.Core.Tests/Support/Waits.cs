using System;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>
/// Waits for a state that no fact announces (a heartbeat's bytes arriving): looks again every couple of milliseconds of
/// real time until it holds, within the harness's real-time deadline. Time on the fake clock does not move meanwhile.
/// </summary>
/// <remarks>
/// It used to look again after a bare <c>Task.Yield</c>, which keeps a core busy for as long as it waits; with every suite
/// of the solution running at once, a dozen such waiters starved the very nodes they waited for until a 15-second deadline
/// passed. A short timer between looks leaves the cores to the nodes.
/// </remarks>
internal static class Waits
{
    private static readonly TimeSpan Recheck = TimeSpan.FromMilliseconds(2);

    public static async Task UntilAsync(Func<bool> condition, string what)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        while (!condition())
        {
            if (deadline.IsCancellationRequested)
                throw new TimeoutException($"Waited {TestMesh.Deadline.TotalSeconds:0} s for {what}, in vain.");
            await PauseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Completes after <see cref="Recheck"/> of real time, without holding a thread meanwhile.</summary>
    /// <returns>The pause.</returns>
    public static Task PauseAsync()
    {
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The source's timer fires once and is then released; nothing is left to dispose.
        var timer = new CancellationTokenSource(Recheck);
        timer.Token.UnsafeRegister(static state => ((TaskCompletionSource)state!).TrySetResult(), paused);
        return paused.Task;
    }
}
