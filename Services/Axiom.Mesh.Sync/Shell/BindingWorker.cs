using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Sync;

/// <summary>
/// One binding at work (SYNC-SERVICE.md §3): one run at a time, triggers coalesced (the watcher after its debounce, the rescan
/// every 10 minutes, the start, a new Version of its Archive, a Package change, <c>bind</c>, <c>scan</c>, a retry while it waits
/// for objects), each run waiting for the connection and for one of the service's run slots. Reports every state with
/// <c>binding</c> events, at most once a second while it runs and always at its end.
/// </summary>
internal sealed class BindingWorker : IAsyncDisposable
{
    private static readonly TimeSpan EventGap = TimeSpan.FromSeconds(1);

    private readonly BindingContext context;
    private readonly BindingRow row;
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private IFolderWatcher? watcher;
    private ITimer? debounce;
    private ITimer? rescan;
    private ITimer? retry;
    private int waits;
    private Task loop = Task.CompletedTask;
    private DateTimeOffset? burstStart;
    private DateTimeOffset lastEvent = DateTimeOffset.MinValue;
    private BindingState state;
    private bool queued;
    private bool running;
    private bool deferred;
    private ObjectHash head;
    private ushort leaves;
    private uint conflicts;
    private GrantRights rights = GrantRights.Write;

    /// <summary>A worker for a binding.</summary>
    /// <param name="context">What every worker shares.</param>
    /// <param name="spec">The binding.</param>
    /// <param name="row">Its row, owned by the worker from now on.</param>
    /// <param name="conflicts">The conflicts recorded for it.</param>
    public BindingWorker(BindingContext context, BindingSpec spec, BindingRow row, uint conflicts)
    {
        this.context = context;
        Spec = spec;
        this.row = row;
        this.conflicts = conflicts;
        state = row.State == 0 ? BindingState.InSync : (BindingState)row.State;
    }

    /// <summary>The binding.</summary>
    public BindingSpec Spec { get; }

    /// <summary>Starts the watcher, the rescan and the loop.</summary>
    public void Start()
    {
        TimeProvider time = context.Options.Time;
        watcher = context.Options.Watchers.Watch(Spec.Directory, Changed);
        if (!watcher.Watching)
            SyncLog.NotWatched(context.Log, Spec.Directory, SyncLimits.Rescan.TotalMinutes);
        debounce = time.CreateTimer(_ => Debounced(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        rescan = time.CreateTimer(_ => Trigger(), null, SyncLimits.Rescan, SyncLimits.Rescan);
        retry = time.CreateTimer(_ => Trigger(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        loop = Task.Run(LoopAsync);
    }

    /// <summary>Queues a run; one already queued absorbs it.</summary>
    public void Trigger()
    {
        lock (gate)
        {
            queued = true;
            deferred = false;
            if (wake.CurrentCount == 0)
                wake.Release();
        }
    }

    /// <summary>Queues a run when the last one could not plan because the Version DAG was behind the Host.</summary>
    public void TriggerIfDeferred()
    {
        bool again;
        lock (gate)
            again = deferred;
        if (again)
            Trigger();
    }

    /// <summary>The binding as the namespace shows it now.</summary>
    /// <returns>The view.</returns>
    public Binding View()
    {
        lock (gate)
        {
            return new Binding
            {
                Directory = Spec.Directory,
                PackageId = Spec.Package,
                Label = Spec.Label,
                Symlinks = Spec.Symlinks,
                Ignore = Spec.Ignore.Patterns,
                State = state,
                Rights = rights,
                Base = new ObjectHash(RowBytes.Hash(row.BaseTree)),
                Head = head,
                Leaves = leaves,
                Files = row.Files,
                Bytes = row.Bytes,
                LocalChanges = row.LocalChanges,
                Conflicts = conflicts,
                LastRun = (ulong)Math.Max(row.LastRunMs, 0),
                Runs = row.Runs,
                Pending = queued || running,
                Error = TextCut.Of(row.Error, SyncLimits.ErrorLength),
            };
        }
    }

    /// <summary>Stops the worker: the run in hand is cancelled, the watcher and timers end.</summary>
    /// <returns>A task that completes when the loop ended.</returns>
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync().ConfigureAwait(false);
        watcher?.Dispose();
        if (debounce is not null)
            await debounce.DisposeAsync().ConfigureAwait(false);
        if (rescan is not null)
            await rescan.DisposeAsync().ConfigureAwait(false);
        if (retry is not null)
            await retry.DisposeAsync().ConfigureAwait(false);
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped while it waited.
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        lock (gate)
            return $"worker of {Spec}: {state}{(running ? ", a run in hand" : "")}{(queued ? ", a run queued" : "")}";
    }

    // A change below the directory: the run starts once it was quiet for 2 seconds, at most 30 seconds after the first.
    private void Changed(bool lost)
    {
        TimeProvider time = context.Options.Time;
        DateTimeOffset now = time.GetUtcNow();
        lock (gate)
        {
            burstStart ??= now;
            DateTimeOffset due = lost ? now : Debounce.Next(burstStart.Value, now);
            debounce?.Change(due - now, Timeout.InfiniteTimeSpan);
        }
    }

    private void Debounced()
    {
        lock (gate)
            burstStart = null;
        Trigger();
    }

    private async Task LoopAsync()
    {
        CancellationToken stopping = stop.Token;
        while (!stopping.IsCancellationRequested)
        {
            await wake.WaitAsync(stopping).ConfigureAwait(false);
            lock (gate)
            {
                queued = false;
                running = true;
            }

            await context.Gate.WaitAsync(stopping).ConfigureAwait(false);
            ServiceSession? session = null;
            CancellationToken ending = CancellationToken.None;
            try
            {
                (session, ending) = await context.Sessions.CurrentAsync(stopping).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, ending);
                ServiceSession on = session;
                RunOutcome outcome = await new SyncRun(on, context.Store, context.Versions, Spec, row, context.Options, s => Report(on, s, final: false)).RunAsync(linked.Token).ConfigureAwait(false);
                await ApplyAsync(on, outcome).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (RunStoppedException)
            {
                // A test stopped the run where a crash would: the worker ends as the process would have.
                break;
            }
            catch (Exception ex) when (ending.IsCancellationRequested && ex is OperationCanceledException or LocalConnectionException or ObjectDisposedException)
            {
                // The connection ended under the run: it comes again on the next connection.
                lock (gate)
                    queued = true;
            }
            catch (Exception ex)
            {
                // Whatever failed, the binding says so and the next trigger tries again; the worker never dies of it.
                SyncLog.RunFailed(context.Log, Spec.Directory, ex.Message);
                await FailAsync(session, $"{ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
            }
            finally
            {
                context.Gate.Release();
                lock (gate)
                    running = false;
            }

            if (session is not null)
                Report(session, StateNow(), final: true);
        }
    }

    // How a run ended: the row's record of it, the views, the events.
    private async Task ApplyAsync(ServiceSession session, RunOutcome outcome)
    {
        System.Collections.Generic.IReadOnlyList<VersionFact> branch = Leaves.Of(context.Versions.Of(Spec.Package), Spec.Label);
        LocalResult<Package> package = await session.Client.SendAsync(new GetPackage { PackageId = Spec.Package }, CancellationToken.None).ConfigureAwait(false);
        lock (gate)
        {
            deferred = outcome.Deferred;
            state = outcome.State;
            head = Leaves.Head(branch)?.Hash ?? default;
            leaves = (ushort)Math.Min(branch.Count, ushort.MaxValue);
            conflicts += (uint)outcome.Conflicts.Count;
            if (package.IsOk)
                rights = package.Value.Rights;
            if (!outcome.Deferred)
            {
                row.State = (byte)outcome.State;
                row.Error = outcome.Error;
                row.LastRunMs = context.Options.Time.GetUtcNow().ToUnixTimeMilliseconds();
                row.Runs++;
                if (outcome.Error.Length == 0)
                {
                    row.Files = outcome.Files;
                    row.Bytes = outcome.Bytes;
                    row.LocalChanges = outcome.LocalChanges;
                }
            }
        }

        if (!outcome.Deferred)
        {
            await context.Store.SaveBindingAsync(row, CancellationToken.None).ConfigureAwait(false);
            SyncLog.RunEnded(context.Log, Spec.Directory, outcome.Plan, outcome.State);
        }

        foreach (ConflictRow conflict in outcome.Conflicts)
            Emit(session, SyncEvents.Conflict, SyncCodec.Encode(new ConflictEvent { Conflict = ConflictViews.Of(conflict, Spec.Directory) }));
        if (outcome.Busy)
            debounce?.Change(SyncLimits.DebounceQuiet, Timeout.InfiniteTimeSpan);

        // Objects no peer supplied yet: nothing announces them, so the binding asks again, waiting longer each time.
        if (outcome.Deferred)
            return;
        waits = outcome.State == BindingState.WaitingForObjects ? waits + 1 : 0;
        if (waits > 0)
            retry?.Change(WaitRetry.After(waits), Timeout.InfiniteTimeSpan);
    }

    private async Task FailAsync(ServiceSession? session, string problem)
    {
        lock (gate)
        {
            state = BindingState.Error;
            row.State = (byte)BindingState.Error;
            row.Error = TextCut.Of(problem, SyncLimits.ErrorLength);
            row.LastRunMs = context.Options.Time.GetUtcNow().ToUnixTimeMilliseconds();
            row.Runs++;
        }

        try
        {
            await context.Store.SaveBindingAsync(row, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            SyncLog.RunFailed(context.Log, Spec.Directory, ex.Message);
        }

        if (session is not null)
            Report(session, BindingState.Error, final: true);
    }

    private BindingState StateNow()
    {
        lock (gate)
            return state;
    }

    // A state the run passed through: an event at most once a second while it runs, and always at its end.
    private void Report(ServiceSession session, BindingState now, bool final)
    {
        DateTimeOffset at = context.Options.Time.GetUtcNow();
        lock (gate)
        {
            if (!final)
                state = now;
            if (!final && at - lastEvent < EventGap)
                return;
            lastEvent = at;
        }

        Emit(session, SyncEvents.Binding, SyncCodec.Encode(new BindingEvent { Binding = View() }));
    }

    private void Emit(ServiceSession session, string name, byte[] payload) => _ = EmitAsync(session, name, payload);

    private async Task EmitAsync(ServiceSession session, string name, byte[] payload)
    {
        try
        {
            LocalResult<NoContent> emitted = await session.EmitAsync(name, default, payload, CancellationToken.None).ConfigureAwait(false);
            if (!emitted.IsOk)
                SyncLog.NotEmitted(context.Log, name, $"{emitted.Status} {emitted.Error?.Message}");
        }
        catch (Exception ex) when (ex is LocalConnectionException or ObjectDisposedException or OperationCanceledException or RequestLimitException)
        {
            SyncLog.NotEmitted(context.Log, name, ex.Message);
        }
    }
}
