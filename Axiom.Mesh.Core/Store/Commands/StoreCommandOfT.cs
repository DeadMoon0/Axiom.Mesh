using System;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>A store command with a result the caller can await.</summary>
/// <typeparam name="TResult">The result.</typeparam>
internal abstract class StoreCommand<TResult> : StoreCommand
{
    private readonly TaskCompletionSource<TResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes with the result once the batch committed, or fails.</summary>
    public Task<TResult> Completion => completion.Task;

    /// <summary>Completes the command.</summary>
    /// <param name="result">The result.</param>
    public void Complete(TResult result) => completion.TrySetResult(result);

    /// <inheritdoc />
    public override void Fail(Exception error) => completion.TrySetException(error);
}
