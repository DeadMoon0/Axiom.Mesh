using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Service;

/// <summary>
/// The callback handler of one connection (LOCAL-SPEC.md §9.8): hands each <c>ForwardedCall</c> to the service with the
/// connection's session, but never before the service's <c>OnConnectedAsync</c> returned. A call that arrives earlier
/// (the Host forwards calls from <c>HelloAck</c> on) waits for it within its deadline; when the session ends before it was
/// ready, the call is answered <c>ServiceUnavailable</c>, as for a service that is not connected.
/// </summary>
internal sealed class ServiceCalls : CallbackHandler
{
    private readonly IMeshService service;
    private readonly TaskCompletionSource<ServiceSession?> session = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ServiceCalls(IMeshService service) => this.service = service;

    /// <summary>The session the calls of this connection belong to, once the service is ready for them.</summary>
    /// <param name="ready">The session, its <c>OnConnectedAsync</c> returned.</param>
    public void Ready(ServiceSession ready) => session.TrySetResult(ready);

    /// <summary>The connection ended before the service was ready: calls waiting, and calls to come, are unavailable.</summary>
    public void Abandon() => session.TrySetResult(null);

    protected override async ValueTask<CallReply> OnForwardedCallAsync(ForwardedCall call, CancellationToken deadline)
    {
        ServiceSession? current = await session.Task.WaitAsync(deadline).ConfigureAwait(false);
        if (current is null)
            return CallReply.Failure(Status.ServiceUnavailable, new Error { Message = "The service lost its connection before it was ready; call again once it reconnected." });
        return await service.OnCallAsync(current, call, deadline).ConfigureAwait(false);
    }
}
