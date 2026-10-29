using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE.Modules;

namespace Axiom.Mesh.Core;

/// <summary>
/// A loop the node runs while its server runs (the dialler, the discovery pump, the evictor): started with the
/// server's services, it waits until the node's ports are bound, runs until the server stops it, and records a
/// failure as a fact instead of ending the process.
/// </summary>
internal sealed class LoopService : IServerService
{
    private readonly string name;
    private readonly Task started;
    private readonly Func<CancellationToken, Task> run;
    private readonly NodeFacts facts;
    private readonly CancellationTokenSource stop = new();
    private Task loop = Task.CompletedTask;

    /// <summary>A loop.</summary>
    /// <param name="name">Its name, for facts.</param>
    /// <param name="started">Completes when the node's ports are bound, so the loop may dial and adopt.</param>
    /// <param name="run">The loop; it returns when its token is cancelled.</param>
    /// <param name="facts">Where a failure is recorded.</param>
    public LoopService(string name, Task started, Func<CancellationToken, Task> run, NodeFacts facts)
    {
        this.name = name;
        this.started = started;
        this.run = run;
        this.facts = facts;
    }

    /// <inheritdoc />
    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        loop = Task.Run(() => RunAsync(stop.Token), CancellationToken.None);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        await stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The stop's deadline passed; the loop is left behind, as LWSE leaves an overrunning service.
        }
        finally
        {
            stop.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await started.WaitAsync(cancellationToken).ConfigureAwait(false);
            await run(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped.
        }
        catch (Exception ex)
        {
            facts.Record(NodeFactKind.HandlerFaulted, 0, null, default, $"{name} stopped: {ex}");
        }
    }
}
