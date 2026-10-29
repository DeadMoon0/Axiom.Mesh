using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>
/// A <c>Fetch</c> handler for the operation tests, until M9 serves the real one: each request starts an operation whose
/// work waits until the test releases it (then Done) or until it is cancelled, and counts the cancellations it observed.
/// </summary>
internal sealed class HeldOperations
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int cancelled;

    /// <summary>Whether the work ends at once (Done) instead of waiting for <see cref="Release"/>.</summary>
    public bool Immediate { get; init; }

    /// <summary>Works that observed their cancellation.</summary>
    public int Cancelled => Volatile.Read(ref cancelled);

    /// <summary>Lets every held work end Done.</summary>
    public void Release() => release.TrySetResult();

    /// <summary>Installs the handler on a node's local port.</summary>
    public void Install(LocalPort port) => port.Routes = port.Routes.With(Fetch.Id, RequestRoutes.Of<Fetch>(StartAsync));

    private ValueTask<LocalAnswer> StartAsync(RequestContext context, uint correlation, Fetch request, CancellationToken cancellationToken)
    {
        OperationTable operations = context.Connection.Operations;
        LocalOperation? operation = operations.TryStart(OperationKind.Fetch, ProgressUnit.Objects, WorkAsync);
        LocalAnswer answer = operation is null
            ? LocalAnswer.Failure(correlation, Status.Busy, "16 operations run already.", string.Empty)
            : LocalAnswer.Ok<Fetch, uint>(correlation, operation.Id).Then(() => operations.Announce(operation));
        return ValueTask.FromResult(answer);
    }

    private async Task<OperationOutcome> WorkAsync(OperationProgress progress, CancellationToken cancellationToken)
    {
        if (!Immediate)
        {
            try
            {
                await release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancelled);
                throw;
            }
        }

        progress.Report(1, 1, "fetched");
        return OperationOutcome.Done(new byte[12]);
    }
}
