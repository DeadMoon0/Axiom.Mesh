using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Testing;

/// <summary>
/// The smallest service there is, for tests of the service runtime, the Host's supervision and the scenario suite: it
/// defines one Package kind (<see cref="Kind"/>, with Entry kind <see cref="EntryKind"/>), remembers every Entry of it in
/// store order (once, deduped by hash), emits <c>seen</c> for each with the Entry's hash as payload, and answers the
/// methods <c>echo</c> (the payload back), <c>count</c> (the Entries seen, a big-endian u64) and <c>write</c> (the payload is a
/// Package id followed by the Entry's payload: it writes an Entry of its kind there and answers with the Entry's hash), as
/// a service writes on behalf of an app.
/// </summary>
/// <remarks>
/// It keeps what it saw in memory, as a cache would: a new instance starts empty and, with the same state, catches up only
/// what its cursor has not reached.
/// </remarks>
public sealed class TestEchoService : IMeshService
{
    /// <summary>The service-defined status of an unknown method (LOCAL-SPEC.md §11.2).</summary>
    public const ushort UnknownMethod = 32_768;

    private readonly object gate = new();
    private readonly List<Entry> seen = [];
    private readonly HashSet<ObjectHash> hashes = [];
    private readonly List<(int Count, TaskCompletionSource Reached)> waiters = [];

    /// <summary>An echo service named <c>echo</c>.</summary>
    public TestEchoService()
        : this(Echo)
    {
    }

    /// <summary>An echo service with another definition: another name, or other kinds.</summary>
    /// <param name="definition">The definition.</param>
    public TestEchoService(ServiceDefinition definition) => Definition = definition ?? throw new ArgumentNullException(nameof(definition));

    /// <summary>The Package kind the echo service defines: 100, a kind no published service uses.</summary>
    public static PackageKind Kind => (PackageKind)100;

    /// <summary>Its Entry kind: 100.</summary>
    public static EntryKind EntryKind => (EntryKind)100;

    /// <summary>The default definition: <c>echo</c>, version <c>test</c>, <see cref="Kind"/> with <see cref="EntryKind"/>.</summary>
    public static ServiceDefinition Echo { get; } = new()
    {
        Name = "echo",
        Version = "test",
        Kinds = [new ServiceKind { Kind = Kind, EntryKinds = [EntryKind] }],
    };

    /// <inheritdoc />
    public ServiceDefinition Definition { get; }

    /// <summary>The Entries seen, in the order the runtime handed them over.</summary>
    public IReadOnlyList<Entry> Seen
    {
        get
        {
            lock (gate)
                return [.. seen];
        }
    }

    /// <summary>How many Entries it has seen.</summary>
    public int Count
    {
        get
        {
            lock (gate)
                return seen.Count;
        }
    }

    /// <summary>Waits until it has seen at least <paramref name="count"/> Entries.</summary>
    /// <param name="count">The count.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when it has.</returns>
    /// <exception cref="TestMeshException">It did not within <see cref="TestMesh.Deadline"/>.</exception>
    public async Task WaitForCountAsync(int count, CancellationToken cancellationToken)
    {
        Task reached;
        lock (gate)
        {
            if (seen.Count >= count)
                return;
            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiters.Add((count, waiter));
            reached = waiter.Task;
        }

        try
        {
            await reached.WaitAsync(TestMesh.Deadline, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new TestMeshException(
                $"The service {Definition.Name} saw {Count} Entries, not {count}, within {TestMesh.Deadline.TotalSeconds:0} s.",
                "Check that the Entries are of its kind and that the node it runs beside stores them (follows the Package).");
        }
    }

    /// <inheritdoc />
    public Task OnConnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.CatchUp.DiscardIndex)
        {
            lock (gate)
            {
                seen.Clear();
                hashes.Clear();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task OnEntryAsync(ServiceSession session, EntryStored stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stored);
        lock (gate)
        {
            if (hashes.Contains(stored.Entry.Hash))
                return;
        }

        // The event first: an Entry counts as seen once everything the handler does for it is done.
        byte[] hash = new byte[Hash32.Length];
        stored.Entry.Hash.Value.CopyTo(hash);
        await session.EmitAsync("seen", stored.Entry.PackageId, hash, cancellationToken).ConfigureAwait(false);
        TaskCompletionSource[] reached;
        lock (gate)
        {
            hashes.Add(stored.Entry.Hash);
            seen.Add(stored.Entry);
            reached = [.. waiters.Where(w => seen.Count >= w.Count).Select(w => w.Reached)];
            waiters.RemoveAll(w => seen.Count >= w.Count);
        }

        foreach (TaskCompletionSource waiter in reached)
            waiter.TrySetResult();
    }

    /// <inheritdoc />
    public Task OnPackageChangedAsync(ServiceSession session, PackageChanged changed, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async ValueTask<CallReply> OnCallAsync(ServiceSession session, ForwardedCall call, CancellationToken deadline)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(call);
        if (call.Method == "write")
            return await WriteAsync(session, call.Payload, deadline).ConfigureAwait(false);
        return Answer(call);
    }

    /// <summary>The payload of a <c>write</c> call: the Package, then the Entry's payload.</summary>
    /// <param name="package">The Package, of <see cref="Kind"/>.</param>
    /// <param name="payload">The Entry's payload.</param>
    /// <returns>The call's payload.</returns>
    public static byte[] WritePayload(PackageId package, ReadOnlySpan<byte> payload)
    {
        byte[] bytes = new byte[Hash32.Length + payload.Length];
        package.Value.CopyTo(bytes);
        payload.CopyTo(bytes.AsSpan(Hash32.Length));
        return bytes;
    }

    /// <inheritdoc />
    public Task OnDisconnectedAsync(ServiceSession session, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public override string ToString() => $"echo service {Definition.Name}, {Count} Entries seen";

    private static async Task<CallReply> WriteAsync(ServiceSession session, ReadOnlyMemory<byte> payload, CancellationToken deadline)
    {
        if (payload.Length < Hash32.Length)
            return CallReply.Failure(Status.Invalid, new Error { Message = "A write names its Package first.", Detail = "payload" });
        var package = new PackageId(new Hash32(payload.Span[..Hash32.Length]));
        var body = new EntryBody(EntryKind, payload[Hash32.Length..].ToArray(), EntryReferences.None, default);
        LocalResult<Entry> written = await session.Client.SendAsync(WriteEntry.Of(package, body), deadline).ConfigureAwait(false);
        if (!written.IsOk)
            return CallReply.Failure(written.Status, written.Error ?? new Error { Message = "The Host refused the write." });
        byte[] hash = new byte[Hash32.Length];
        written.Value.Hash.Value.CopyTo(hash);
        return CallReply.Ok(hash);
    }

    private CallReply Answer(ForwardedCall call)
    {
        switch (call.Method)
        {
            case "echo":
                return CallReply.Ok(call.Payload);
            case "count":
                byte[] count = new byte[8];
                BinaryPrimitives.WriteUInt64BigEndian(count, (ulong)Count);
                return CallReply.Ok(count);
            default:
                return CallReply.Failure((Status)UnknownMethod, new Error { Message = $"The echo service has no method '{call.Method}'.", Detail = "method" });
        }
    }
}
