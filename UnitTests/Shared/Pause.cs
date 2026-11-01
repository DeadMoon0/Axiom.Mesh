using System;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Tests.Shared;

/// <summary>
/// A short pause of real time between two looks at a state no event announces (a service's status asked again), without
/// holding a thread meanwhile. A bare <c>Task.Yield</c> between looks keeps a core busy and floods the asked service with
/// requests; with every suite of the solution running at once that starved the nodes being waited for (Core.Tests'
/// <c>Waits</c> learnt it first).
/// </summary>
internal static class Pause
{
    /// <summary>How long one pause lasts.</summary>
    public static readonly TimeSpan Recheck = TimeSpan.FromMilliseconds(20);

    /// <summary>Completes after <see cref="Recheck"/> of real time.</summary>
    /// <returns>The pause.</returns>
    public static Task ShortAsync()
    {
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The source's timer fires once and is then released; nothing is left to dispose.
        var timer = new CancellationTokenSource(Recheck);
        timer.Token.UnsafeRegister(static state => ((TaskCompletionSource)state!).TrySetResult(), paused);
        return paused.Task;
    }
}
