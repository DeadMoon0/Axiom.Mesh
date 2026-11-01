using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Service.Tests.Support;

/// <summary>
/// The echo service whose <c>OnConnectedAsync</c> holds until the test releases it, and which records, for every call it
/// answers, whether its <c>OnConnectedAsync</c> had returned by then.
/// </summary>
internal sealed class SlowConnectingService(TestEchoService inner) : IMeshService
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int connected;
    private int callsBeforeConnected;
    private int calls;

    public SlowConnectingService()
        : this(new TestEchoService())
    {
    }

    public ServiceDefinition Definition => inner.Definition;

    /// <summary>Completes once the runtime called <c>OnConnectedAsync</c>.</summary>
    public Task Entered => entered.Task;

    /// <summary>Calls answered while <c>OnConnectedAsync</c> had not returned yet.</summary>
    public int CallsBeforeConnected => Volatile.Read(ref callsBeforeConnected);

    /// <summary>Calls answered.</summary>
    public int Calls => Volatile.Read(ref calls);

    /// <summary>Lets <c>OnConnectedAsync</c> return.</summary>
    public void Release() => release.TrySetResult();

    public async Task OnConnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        await inner.OnConnectedAsync(session, cancellationToken);
        entered.TrySetResult();
        await release.Task.WaitAsync(cancellationToken);
        Volatile.Write(ref connected, 1);
    }

    public Task OnEntryAsync(ServiceSession session, EntryStored stored, CancellationToken cancellationToken) => inner.OnEntryAsync(session, stored, cancellationToken);

    public Task OnPackageChangedAsync(ServiceSession session, PackageChanged changed, CancellationToken cancellationToken) => inner.OnPackageChangedAsync(session, changed, cancellationToken);

    public ValueTask<CallReply> OnCallAsync(ServiceSession session, ForwardedCall call, CancellationToken deadline)
    {
        if (Volatile.Read(ref connected) == 0)
            Interlocked.Increment(ref callsBeforeConnected);
        Interlocked.Increment(ref calls);
        return inner.OnCallAsync(session, call, deadline);
    }

    public Task OnDisconnectedAsync(ServiceSession session, CancellationToken cancellationToken) => inner.OnDisconnectedAsync(session, cancellationToken);
}
