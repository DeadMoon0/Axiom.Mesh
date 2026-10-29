using System;
using System.Threading.Tasks;

namespace Axiom.Mesh.Local;

/// <summary>A pending request of one class, with the task its sender awaits.</summary>
/// <typeparam name="TRequest">The request.</typeparam>
/// <typeparam name="TOk">Its OK body.</typeparam>
internal sealed class PendingRequest<TRequest, TOk> : PendingRequest
    where TRequest : LocalRequest, IRequest<TRequest, TOk>
{
    private readonly TaskCompletionSource<LocalResult<TOk>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The result, once the response arrived.</summary>
    public Task<LocalResult<TOk>> Task => completion.Task;

    public override bool Complete(Response response)
    {
        if (response.IsOk)
        {
            if (!response.TryReadOk<TRequest, TOk>(out TOk? ok))
                return false;
            completion.TrySetResult(LocalResult<TOk>.Ok(ok));
            return true;
        }

        if (!response.TryReadError(out Error? error))
            return false;
        completion.TrySetResult(LocalResult<TOk>.Failure(response.Status, error));
        return true;
    }

    public override void Fail(Exception error) => completion.TrySetException(error);
}
