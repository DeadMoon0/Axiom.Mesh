using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Local;

/// <summary>
/// What a service does with the callbacks the Host sends it (LOCAL-SPEC.md §3.6, §9.8). Give an instance to
/// <see cref="LocalClientOptions.Callbacks"/>: the client keeps reading the pipe while a handler runs, runs each on a
/// worker with a deadline of 60 seconds linked to the connection's end, and sends the one <c>Reply</c> it returns. A handler
/// that throws answers <c>IoError</c> with the fault's type; one stopped by its deadline answers nothing, because the Host
/// already told the caller <c>ServiceTimeout</c>.
/// </summary>
/// <remarks>
/// A handler may send requests on the same client (it runs off the read loop, so their responses arrive), but it must not
/// wait for anything that only another callback would bring.
/// </remarks>
public abstract class CallbackHandler
{
    /// <summary>A handler; override the callbacks the service serves.</summary>
    protected CallbackHandler()
    {
    }

    /// <summary>
    /// A <c>ServiceCall</c> from a user client, forwarded verbatim (§9.8). The default answers <c>UnknownRequest</c>: a
    /// service that serves calls overrides it.
    /// </summary>
    /// <param name="call">The call: the caller's number and name, the method and the payload.</param>
    /// <param name="deadline">Cancelled at the 60-second deadline and when the connection ends.</param>
    /// <returns>The reply: the service's payload, or a status with its explanation.</returns>
    protected internal virtual ValueTask<CallReply> OnForwardedCallAsync(ForwardedCall call, CancellationToken deadline) =>
        ValueTask.FromResult(CallReply.Failure(Status.UnknownRequest, new Error { Message = $"This service has no method '{call?.Method}'." }));
}
