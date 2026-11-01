using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The connection the bindings run on, while there is one: set once the service is ready on a connection, cleared when it
/// ends. A run waits for one, and is cancelled when the one it runs on ends.
/// </summary>
internal sealed class SessionHolder
{
    private readonly object gate = new();
    private TaskCompletionSource<(ServiceSession Session, CancellationToken Ending)> next = New();
    private CancellationTokenSource? ending;

    /// <summary>The connection, once there is one, and a token cancelled when it ends.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The session and its end.</returns>
    public Task<(ServiceSession Session, CancellationToken Ending)> CurrentAsync(CancellationToken cancellationToken)
    {
        lock (gate)
            return next.Task.WaitAsync(cancellationToken);
    }

    /// <summary>The service is ready on a connection.</summary>
    /// <param name="session">The session.</param>
    public void Set(ServiceSession session)
    {
        lock (gate)
        {
            ending?.Cancel();
            ending = new CancellationTokenSource();
            if (next.Task.IsCompleted)
                next = New();
            next.TrySetResult((session, ending.Token));
        }
    }

    /// <summary>A connection ended: runs on it are cancelled, new runs wait for the next.</summary>
    /// <param name="session">The session that ended.</param>
    public void Clear(ServiceSession session)
    {
        lock (gate)
        {
            if (!next.Task.IsCompletedSuccessfully || next.Task.Result.Session != session)
                return;
            ending?.Cancel();
            ending = null;
            next = New();
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        lock (gate)
            return next.Task.IsCompletedSuccessfully ? "connected" : "waiting for a connection";
    }

    private static TaskCompletionSource<(ServiceSession, CancellationToken)> New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
