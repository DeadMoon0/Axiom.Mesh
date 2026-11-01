using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Testing;

/// <summary>A service seen from the test: every handler forwarded, and <see cref="Connected"/> completed once it was told it is connected.</summary>
internal sealed class ObservedService(IMeshService inner) : IMeshService
{
    private readonly TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once <c>OnConnectedAsync</c> returned the first time.</summary>
    public Task Connected => connected.Task;

    public ServiceDefinition Definition => inner.Definition;

    public async Task OnConnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        await inner.OnConnectedAsync(session, cancellationToken).ConfigureAwait(false);
        connected.TrySetResult();
    }

    public Task OnEntryAsync(ServiceSession session, EntryStored stored, CancellationToken cancellationToken) => inner.OnEntryAsync(session, stored, cancellationToken);

    public Task OnPackageChangedAsync(ServiceSession session, PackageChanged changed, CancellationToken cancellationToken) => inner.OnPackageChangedAsync(session, changed, cancellationToken);

    public ValueTask<CallReply> OnCallAsync(ServiceSession session, ForwardedCall call, CancellationToken deadline) => inner.OnCallAsync(session, call, deadline);

    public Task OnDisconnectedAsync(ServiceSession session, CancellationToken cancellationToken) => inner.OnDisconnectedAsync(session, cancellationToken);
}
