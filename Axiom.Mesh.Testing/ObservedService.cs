using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Testing;

/// <summary>
/// A service as the harness runs it: every member forwarded to the service under test, the first <c>OnConnectedAsync</c>
/// awaited by <see cref="RunningService.WhenConnectedAsync"/>, and a count of the handlers in flight so a wait that ran out
/// can say where the service stands. Every member of <see cref="IMeshService"/> must be forwarded here, or its default runs.
/// </summary>
internal sealed class ObservedService(IMeshService inner) : IMeshService
{
    private readonly TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int inHandler;
    private int connections;
    private int disconnections;

    /// <summary>Completes once the service's first <c>OnConnectedAsync</c> returned.</summary>
    public Task Connected => connected.Task;

    /// <inheritdoc />
    public ServiceDefinition Definition => inner.Definition;

    /// <inheritdoc />
    public async Task OnConnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref connections);
        await Observed(inner.OnConnectedAsync(session, cancellationToken)).ConfigureAwait(false);
        connected.TrySetResult();
    }

    /// <inheritdoc />
    public Task OnEntryAsync(ServiceSession session, EntryStored stored, CancellationToken cancellationToken) => Observed(inner.OnEntryAsync(session, stored, cancellationToken));

    /// <inheritdoc />
    public Task OnEntriesAsync(ServiceSession session, IReadOnlyList<EntryStored> stored, CancellationToken cancellationToken) => Observed(inner.OnEntriesAsync(session, stored, cancellationToken));

    /// <inheritdoc />
    public Task OnPackageChangedAsync(ServiceSession session, PackageChanged changed, CancellationToken cancellationToken) => Observed(inner.OnPackageChangedAsync(session, changed, cancellationToken));

    /// <inheritdoc />
    public async ValueTask<CallReply> OnCallAsync(ServiceSession session, ForwardedCall call, CancellationToken deadline)
    {
        Interlocked.Increment(ref inHandler);
        try
        {
            return await inner.OnCallAsync(session, call, deadline).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref inHandler);
        }
    }

    /// <inheritdoc />
    public async Task OnDisconnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        await Observed(inner.OnDisconnectedAsync(session, cancellationToken)).ConfigureAwait(false);
        Interlocked.Increment(ref disconnections);
    }

    /// <summary>Where the service stands, for a harness wait that ran out: handlers in flight, sessions, the service itself.</summary>
    /// <returns>One line.</returns>
    public override string ToString() =>
        $"{Volatile.Read(ref inHandler)} handler(s) in flight, connected {Volatile.Read(ref connections)} time(s), disconnected {Volatile.Read(ref disconnections)} time(s); {inner}";

    // The runtime awaits every handler, so the count in flight says whether the service or the runtime holds up an exit.
    private async Task Observed(Task handler)
    {
        Interlocked.Increment(ref inHandler);
        try
        {
            await handler.ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref inHandler);
        }
    }
}
