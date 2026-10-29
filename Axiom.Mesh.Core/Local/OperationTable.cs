using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The operations of one connection (LOCAL-SPEC.md §8): at most 16 running, the records of the last 48 ended kept, ids
/// chosen here and never reused on the connection. Every state change emits an <c>OperationChanged</c>, progress at most
/// once per second, the final event always; nothing is emitted before the <c>Response</c> that started the operation was
/// written (§7.2). <c>CancelOperation</c>, <c>Drain</c> and the connection's end cancel an operation: it ends Cancelled
/// at once, and what its work already did stays done (§8.2).
/// </summary>
internal sealed class OperationTable : IDisposable
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);

    private readonly object gate = new();
    private readonly Dictionary<uint, LocalOperation> running = [];
    private readonly LinkedList<Operation> ended = new();
    private readonly TimeProvider time;
    private readonly Action<Operation> emit;
    private readonly CancellationToken connection;
    private uint lastId;
    private bool closed;

    /// <summary>The table of one connection.</summary>
    /// <param name="time">The node's clock.</param>
    /// <param name="emit">Hands an <c>OperationChanged</c> to the connection's subscriptions of topic operations.</param>
    /// <param name="connection">Cancelled when the connection ends.</param>
    public OperationTable(TimeProvider time, Action<Operation> emit, CancellationToken connection)
    {
        this.time = time;
        this.emit = emit;
        this.connection = connection;
    }

    /// <summary>Operations running now.</summary>
    public int Running
    {
        get
        {
            lock (gate)
                return running.Count;
        }
    }

    /// <summary>Starts an operation, unless 16 run already (§8.1, <c>Busy</c>).</summary>
    /// <param name="kind">Its kind.</param>
    /// <param name="unit">What it counts.</param>
    /// <param name="work">The work; it reports progress and honours the token.</param>
    /// <returns>The operation, whose events wait for <see cref="Announce"/>; <see langword="null"/> when busy.</returns>
    public LocalOperation? TryStart(OperationKind kind, ProgressUnit unit, Func<OperationProgress, CancellationToken, Task<OperationOutcome>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        LocalOperation operation;
        lock (gate)
        {
            if (closed || running.Count >= Limits.MaxRunningOperations)
                return null;
            operation = new LocalOperation(++lastId, kind, unit, Now(), connection);
            running.Add(operation.Id, operation);
        }

        _ = RunAsync(operation, work);
        return operation;
    }

    /// <summary>
    /// The <c>Response</c> that started <paramref name="operation"/> was written: its first event (and, if it ended
    /// meanwhile, its final one) may follow now.
    /// </summary>
    /// <param name="operation">The operation.</param>
    public void Announce(LocalOperation operation)
    {
        Operation? first;
        Operation? last = null;
        lock (gate)
        {
            operation.Announced = true;
            operation.LastEmittedMs = Now();
            first = operation.HasEnded ? Started(operation) : operation.Record();
            if (operation.HasEnded)
                last = operation.Record();

            // Under the lock, so that no other event of the operation overtakes its first one.
            emit(first);
            if (last is not null)
                emit(last);
        }
    }

    /// <summary>The record of an operation of this connection, running or among the ended ones kept.</summary>
    /// <param name="id">The operation id.</param>
    /// <returns>The record; <see langword="null"/> when unknown (<c>NotFound</c>).</returns>
    public Operation? Get(uint id)
    {
        lock (gate)
        {
            if (running.TryGetValue(id, out LocalOperation? operation))
                return operation.Record();
            return ended.FirstOrDefault(o => o.OperationId == id);
        }
    }

    /// <summary>Every operation of this connection, running and ended, ascending by id (§8.5).</summary>
    /// <returns>At most 64 records.</returns>
    public IReadOnlyList<Operation> List()
    {
        lock (gate)
            return [.. running.Values.Select(o => o.Record()).Concat(ended).OrderBy(o => o.OperationId)];
    }

    /// <summary>Cancels an operation (§8.5): it ends Cancelled now; an ended one is answered unchanged.</summary>
    /// <param name="id">The operation id.</param>
    /// <returns>The record at the time of the answer; <see langword="null"/> when unknown.</returns>
    public Operation? Cancel(uint id)
    {
        LocalOperation? operation;
        lock (gate)
        {
            if (!running.TryGetValue(id, out operation))
                return ended.FirstOrDefault(o => o.OperationId == id);
        }

        return End(operation, null) ?? Get(id);
    }

    /// <summary>Cancels every running operation, for <c>Drain</c> (§4.5): each ends Cancelled with its final event.</summary>
    public void CancelAll()
    {
        LocalOperation[] all;
        lock (gate)
            all = [.. running.Values];
        foreach (LocalOperation operation in all)
            End(operation, null);
    }

    /// <summary>The connection ended: every operation is cancelled and nothing more is emitted.</summary>
    public void Dispose()
    {
        LocalOperation[] all;
        lock (gate)
        {
            closed = true;
            all = [.. running.Values];
        }

        foreach (LocalOperation operation in all)
            End(operation, null);
    }

    /// <summary>A progress report from an operation's work.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="done">Units done.</param>
    /// <param name="total">Units in all.</param>
    /// <param name="message">For display.</param>
    internal void Progress(LocalOperation operation, ulong done, ulong total, string message)
    {
        Operation? record = null;
        lock (gate)
        {
            if (operation.HasEnded)
                return;
            long now = Now();
            operation.Done = done;
            operation.Total = total;
            operation.Message = message ?? string.Empty;
            operation.UpdatedMs = now;
            if (operation.Announced && !closed && now - operation.LastEmittedMs >= (long)ProgressInterval.TotalMilliseconds)
            {
                operation.LastEmittedMs = now;
                record = operation.Record();
                emit(record);
            }
        }
    }

    private static Operation Started(LocalOperation operation) => operation.Record() with
    {
        State = OperationState.Running,
        Status = Status.Ok,
        Result = default,
        Updated = LocalRecords.Milliseconds(operation.StartedMs),
    };

    private async Task RunAsync(LocalOperation operation, Func<OperationProgress, CancellationToken, Task<OperationOutcome>> work)
    {
        OperationOutcome? outcome = null;
        try
        {
            outcome = await Task.Run(() => work(new OperationProgress(this, operation), operation.Cancellation.Token), CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operation.Cancellation.IsCancellationRequested)
        {
            // Cancelled: End records it.
        }
        catch (Exception ex)
        {
            outcome = OperationOutcome.Failed(Status.IoError, ex.Message);
        }

        End(operation, outcome);
        operation.Dispose();
    }

    // Ends an operation once: with the work's outcome, or Cancelled when there is none. Returns the final record, or null
    // when it had ended already.
    private Operation? End(LocalOperation operation, OperationOutcome? outcome)
    {
        Operation record;
        bool announce;
        lock (gate)
        {
            if (operation.HasEnded)
                return null;
            operation.UpdatedMs = Now();
            if (outcome is null)
            {
                operation.State = OperationState.Cancelled;
                operation.Status = Status.Cancelled;
            }
            else if (outcome.Status == Status.Ok)
            {
                operation.State = OperationState.Done;
                operation.Result = outcome.Result;
            }
            else
            {
                operation.State = OperationState.Failed;
                operation.Status = outcome.Status;
                operation.Message = outcome.Message;
            }

            record = operation.Record();
            running.Remove(operation.Id);
            ended.AddLast(record);
            while (ended.Count > Limits.MaxEndedOperations)
                ended.RemoveFirst();
            announce = operation.Announced && !closed;
            if (announce)
                emit(record);
        }

        try
        {
            operation.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The work ended meanwhile and released its token.
        }

        return record;
    }

    private long Now() => time.GetUtcNow().ToUnixTimeMilliseconds();
}
