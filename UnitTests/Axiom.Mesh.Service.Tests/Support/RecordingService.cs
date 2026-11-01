using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Service.Tests.Support;

/// <summary>
/// The echo service with what the runtime told it on top: every catch-up plan, every Package change, every disconnect,
/// and a failure it can be told to raise once on a given Entry.
/// </summary>
internal sealed class RecordingService(TestEchoService inner) : IMeshService
{
    private int failOn = -1;

    public RecordingService()
        : this(new TestEchoService())
    {
    }

    public TestEchoService Echo => inner;

    public ConcurrentQueue<IndexCatchUp> Plans { get; } = new();

    public ConcurrentQueue<PackageChanged> Changes { get; } = new();

    /// <summary>The size of every page of the catch-up the runtime handed over at once.</summary>
    public ConcurrentQueue<int> Pages { get; } = new();

    public int Disconnects;

    public ServiceDefinition Definition => inner.Definition;

    /// <summary>The next time the Entry with this store sequence is handed over, the handler throws (once).</summary>
    public void FailOnceAt(ulong storeSeq) => Volatile.Write(ref failOn, (int)storeSeq);

    public Task OnConnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        Plans.Enqueue(session.CatchUp);
        return inner.OnConnectedAsync(session, cancellationToken);
    }

    public Task OnEntryAsync(ServiceSession session, EntryStored stored, CancellationToken cancellationToken)
    {
        if ((ulong)Volatile.Read(ref failOn) == stored.Entry.StoreSeq && Interlocked.Exchange(ref failOn, -1) >= 0)
            throw new InvalidOperationException("The index could not be written (the test said so).");
        return inner.OnEntryAsync(session, stored, cancellationToken);
    }

    public async Task OnEntriesAsync(ServiceSession session, System.Collections.Generic.IReadOnlyList<EntryStored> stored, CancellationToken cancellationToken)
    {
        Pages.Enqueue(stored.Count);
        foreach (EntryStored one in stored)
            await OnEntryAsync(session, one, cancellationToken);
    }

    public Task OnPackageChangedAsync(ServiceSession session, PackageChanged changed, CancellationToken cancellationToken)
    {
        Changes.Enqueue(changed);
        return Task.CompletedTask;
    }

    public ValueTask<CallReply> OnCallAsync(ServiceSession session, ForwardedCall call, CancellationToken deadline) => inner.OnCallAsync(session, call, deadline);

    public Task OnDisconnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Disconnects);
        return Task.CompletedTask;
    }
}
