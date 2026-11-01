using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Service;

/// <summary>
/// The callback handler of one connection (LOCAL-SPEC.md §9.8): hands each <c>ForwardedCall</c> to the service with the
/// connection's session. A call that arrives between <c>HelloAck</c> and the session's making waits for it, within its
/// deadline.
/// </summary>
internal sealed class ServiceCalls : CallbackHandler
{
    private readonly IMeshService service;
    private readonly TaskCompletionSource<ServiceSession> session = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ServiceCalls(IMeshService service) => this.service = service;

    /// <summary>The session the calls of this connection belong to, once it exists.</summary>
    /// <param name="made">The session.</param>
    public void Attach(ServiceSession made) => session.TrySetResult(made);

    protected override async ValueTask<CallReply> OnForwardedCallAsync(ForwardedCall call, CancellationToken deadline)
    {
        ServiceSession current = await session.Task.WaitAsync(deadline).ConfigureAwait(false);
        return await service.OnCallAsync(current, call, deadline).ConfigureAwait(false);
    }
}
