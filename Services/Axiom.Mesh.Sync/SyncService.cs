using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The sync service (SYNC-SERVICE.md): it defines the Archive kind with the Version Entry, binds directories to Archives, and
/// keeps each in step with its branch both ways: local changes published as Versions, the branch's leaves merged by SPEC.md §9.5
/// and written into the directory. It keeps its bindings and the Version DAG in its own database and offers them to every app as
/// the namespace <c>axiom-mesh-sync/1</c>, methods over <c>ServiceCall</c> and events over <c>Emit</c>. It owns no user interface
/// (S-D13). Run by <see cref="ServiceProgram"/>: spawned by the Host as <c>axiom-mesh-sync</c>, or in process beside a test node.
/// </summary>
public sealed class SyncService : IMeshService
{
    private readonly SyncServiceOptions options;
    private readonly SessionHolder sessions = new();
    private readonly SemaphoreSlim runs = new(BindingContext.RunsAtOnce, BindingContext.RunsAtOnce);
    private readonly BindingBook book = new();
    private readonly object gate = new();
    private BindingContext? context;
    private ServiceSession? current;

    /// <summary>The sync service on the system's clock, reading a file once it was quiet for two seconds.</summary>
    public SyncService()
        : this(SyncServiceOptions.Default)
    {
    }

    /// <summary>The sync service with options: a test's fake clock, a stability time of zero.</summary>
    /// <param name="options">The options.</param>
    public SyncService(SyncServiceOptions options) => this.options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public ServiceDefinition Definition => SyncProgram.Definition;

    /// <inheritdoc />
    public async Task OnConnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        BindingContext shared = context ?? await OpenAsync(session, cancellationToken).ConfigureAwait(false);
        (int schema, Hash32 epoch, _) = await shared.Store.ReadMetaAsync(cancellationToken).ConfigureAwait(false);
        if (schema == 0 || epoch == default)
        {
            await shared.Store.SetEpochAsync(session.StoreEpoch, cancellationToken).ConfigureAwait(false);
        }
        else if (epoch != session.StoreEpoch)
        {
            // LOCAL-SPEC.md §9.3: another store epoch; the DAG goes, the bases and fingerprints (the disk) stay.
            SyncLog.EpochChanged(session.Log);
            await book.StopAllAsync().ConfigureAwait(false);
            await shared.Store.DiscardVersionsAsync(session.StoreEpoch, cancellationToken).ConfigureAwait(false);
            shared.Versions.Clear();
            await StartWorkersAsync(shared, cancellationToken).ConfigureAwait(false);
        }

        lock (gate)
            current = session;
        sessions.Set(session);
        SyncLog.Connected(session.Log, book.Count, shared.Versions.ToString());
        book.TriggerAll();
    }

    /// <inheritdoc />
    public Task OnEntryAsync(ServiceSession session, EntryStored stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return TakeInAsync([stored.Entry], cancellationToken);
    }

    /// <inheritdoc />
    public Task OnEntriesAsync(ServiceSession session, IReadOnlyList<EntryStored> stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return TakeInAsync([.. stored.Select(s => s.Entry)], cancellationToken);
    }

    /// <inheritdoc />
    public Task OnPackageChangedAsync(ServiceSession session, PackageChanged changed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changed);
        book.TriggerPackage(changed.Package.PackageId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<CallReply> OnCallAsync(ServiceSession session, ForwardedCall call, CancellationToken deadline)
    {
        BindingContext shared = context ?? throw new InvalidOperationException("The runtime handed the sync service a call before it connected.");
        return await new SyncHandlers(session, shared, book).CallAsync(call, deadline).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task OnDisconnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (current == session)
                current = null;
        }

        sessions.Clear(session);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"sync service, {book}, {sessions}";

    private async Task TakeInAsync(IReadOnlyList<Entry> entries, CancellationToken cancellationToken)
    {
        BindingContext shared = context ?? throw new InvalidOperationException("The runtime handed the sync service an Entry before it connected.");
        IReadOnlyList<PackageId> touched = await shared.Versions.TakeInAsync(entries, fromStream: true, cancellationToken).ConfigureAwait(false);
        foreach (PackageId package in touched)
            book.TriggerPackage(package);
        book.TriggerDeferred();
    }

    // The database, the DAG and the workers of the bindings it holds, once per process.
    private async Task<BindingContext> OpenAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        SyncStore store = await SyncStore.OpenAsync(session.State.Database, cancellationToken).ConfigureAwait(false);
        VersionIndex versions = await VersionIndex.LoadAsync(store, cancellationToken).ConfigureAwait(false);
        var made = new BindingContext(store, versions, options, sessions, runs, session.Log);
        context = made;
        await StartWorkersAsync(made, cancellationToken).ConfigureAwait(false);
        return made;
    }

    private async Task StartWorkersAsync(BindingContext shared, CancellationToken cancellationToken)
    {
        foreach (BindingRow row in await shared.Store.BindingsAsync(cancellationToken).ConfigureAwait(false))
        {
            int conflicts = await shared.Store.ConflictCountAsync(row.Id, cancellationToken).ConfigureAwait(false);
            book.Add(new BindingWorker(shared, BindingSpec.Of(row), row, (uint)conflicts));
        }
    }
}
