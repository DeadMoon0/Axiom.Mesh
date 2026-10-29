using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// The single writer of the store and the index (plan decision D8). Every change arrives as a <see cref="StoreCommand"/>
/// on one bounded queue; a loop drains up to 256 commands or 8 MiB of object bytes, writes the new objects' files,
/// applies everything in one database transaction, swaps in a new <see cref="TrustSnapshot"/> when trust changed,
/// publishes the <see cref="ChangeSet"/>, and only then completes the commands. Readers never take a lock: they read
/// <see cref="Trust"/>, <see cref="Totals"/> and the database's snapshots.
/// </summary>
internal sealed class StoreWriter : IAsyncDisposable
{
    /// <summary>The queue's bound; a full queue makes senders wait.</summary>
    public const int QueueCapacity = 4096;

    /// <summary>The most commands in one batch.</summary>
    public const int MaxBatchCommands = 256;

    /// <summary>The most object bytes in one batch, unless its first command alone is larger.</summary>
    public const long MaxBatchBytes = 8 * 1_048_576;

    private readonly Channel<StoreCommand> queue = Channel.CreateBounded<StoreCommand>(new BoundedChannelOptions(QueueCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
    });

    private readonly MeshDatabase database;
    private readonly IObjectFiles files;
    private readonly TimeProvider time;
    private readonly ChangeBus bus;
    private readonly Task loop;
    private WriterState state;
    private TrustSnapshot trust;
    private StoreTotals totals;
    private Exception? fault;

    private StoreWriter(MeshDatabase database, IObjectFiles files, StoreOptions options, TimeProvider time, ChangeBus bus, Promises promises, WriterState state)
    {
        this.database = database;
        this.files = files;
        Options = options;
        this.time = time;
        this.bus = bus;
        Promises = promises;
        this.state = state;
        trust = state.Snapshot;
        totals = TotalsOf(state, options.BudgetBytes);
        loop = Task.Run(RunAsync);
    }

    /// <summary>The trust that holds now: read lock-free by TLS callbacks, sessions, intake and the local port.</summary>
    public TrustSnapshot Trust => Volatile.Read(ref trust);

    /// <summary>The store's totals after the last committed batch.</summary>
    public StoreTotals Totals => Volatile.Read(ref totals);

    /// <summary>The store epoch (LOCAL-SPEC.md §9.3).</summary>
    public Hash32 Epoch => database.Epoch;

    /// <summary>The database the index lives in, for readers.</summary>
    public Database Database => database.Database;

    /// <summary>The device class and budget.</summary>
    public StoreOptions Options { get; }

    /// <summary>The objects an outstanding <c>Want</c> protects from eviction.</summary>
    public Promises Promises { get; }

    /// <summary>Loads the index's state and starts the writer.</summary>
    /// <param name="database">The database, its tables ensured.</param>
    /// <param name="files">The object files.</param>
    /// <param name="options">The device class and budget.</param>
    /// <param name="time">The node's clock; every timestamp the index keeps comes from it.</param>
    /// <param name="bus">Where change sets are published.</param>
    /// <param name="promises">The objects an outstanding <c>Want</c> protects from eviction.</param>
    /// <param name="cancellationToken">Cancels loading.</param>
    /// <returns>The running writer.</returns>
    /// <exception cref="MeshStoreException">A current Roster, Manifest or Grant's file is missing or damaged.</exception>
    public static async Task<StoreWriter> StartAsync(MeshDatabase database, IObjectFiles files, StoreOptions options, TimeProvider time, ChangeBus bus, Promises promises, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(promises);
        WriterState state = await WriterState.LoadAsync(database.Database, files, cancellationToken).ConfigureAwait(false);
        return new StoreWriter(database, files, options, time, bus, promises, state);
    }

    /// <summary>Queues a command and waits until its batch committed.</summary>
    /// <typeparam name="T">The command's result.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancels waiting for room in the queue; a queued command is not withdrawn.</param>
    /// <returns>The command's result.</returns>
    /// <exception cref="ObjectDisposedException">The writer stopped.</exception>
    public async Task<T> SendAsync<T>(StoreCommand<T> command, CancellationToken cancellationToken)
    {
        Task<T> completion = await EnqueueAsync(command, cancellationToken).ConfigureAwait(false);
        return await completion.ConfigureAwait(false);
    }

    /// <summary>Queues a command without waiting for its batch.</summary>
    /// <typeparam name="T">The command's result.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancels waiting for room in the queue.</param>
    /// <returns>The command's completion.</returns>
    /// <exception cref="ObjectDisposedException">The writer stopped.</exception>
    public async ValueTask<Task<T>> EnqueueAsync<T>(StoreCommand<T> command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            await queue.Writer.WriteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            command.Release();
            throw new ObjectDisposedException(nameof(StoreWriter), "The store writer stopped; nothing more is stored.");
        }
        catch (OperationCanceledException)
        {
            command.Release();
            throw;
        }

        return command.Completion;
    }

    /// <summary>Queues a command when there is room, without waiting; a use that may be lost (a touch).</summary>
    /// <param name="command">The command.</param>
    /// <returns><see langword="true"/> when queued.</returns>
    public bool TryPost(StoreCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return queue.Writer.TryWrite(command);
    }

    /// <summary>Waits until every command queued before this call is committed.</summary>
    /// <param name="cancellationToken">Cancels waiting for room in the queue.</param>
    /// <returns>A task that completes then.</returns>
    public Task FlushAsync(CancellationToken cancellationToken) => SendAsync(new TouchCommand(default), cancellationToken);

    /// <summary>Stops taking commands, applies and commits every command already queued, and ends the loop.</summary>
    /// <returns>A task that completes when the last batch committed.</returns>
    public async Task StopAsync()
    {
        queue.Writer.TryComplete();
        await loop.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private static StoreTotals TotalsOf(WriterState state, long budget) =>
        new(state.Objects, state.Bytes, state.PinnedBytes, budget, state.StoreSeq);

    private async Task RunAsync()
    {
        var batch = new List<StoreCommand>(MaxBatchCommands);
        while (await queue.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            long bytes = 0;
            while (batch.Count < MaxBatchCommands
                && queue.Reader.TryPeek(out StoreCommand? next)
                && (batch.Count == 0 || bytes + next.Bytes <= MaxBatchBytes)
                && queue.Reader.TryRead(out next))
            {
                batch.Add(next);
                bytes += next.Bytes;
            }

            await ApplyAsync(batch).ConfigureAwait(false);
            foreach (StoreCommand command in batch)
                command.Release();
            batch.Clear();
        }
    }

    private async Task ApplyAsync(List<StoreCommand> batch)
    {
        if (fault is not null)
        {
            Fail(batch, fault);
            return;
        }

        StoreBatch applied;
        try
        {
            await using WriteTransaction transaction = await database.Database.BeginWriteAsync(BatchTables.Names, TransactionOptions.Default, CancellationToken.None).ConfigureAwait(false);
            applied = new StoreBatch(new BatchTables(transaction), state, files, Options, Promises, time.GetUtcNow().ToUnixTimeMilliseconds());
            foreach (StoreCommand command in batch)
                await applied.ApplyAsync(command).ConfigureAwait(false);
            await applied.FinishAsync().ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Fail(batch, e);
            await ReloadAsync().ConfigureAwait(false);
            return;
        }

        if (applied.Changes.TrustChanged)
        {
            state.Snapshot = state.Trust.Build();
            Volatile.Write(ref trust, state.Snapshot);
        }

        StoreTotals now = TotalsOf(state, Options.BudgetBytes);
        Volatile.Write(ref totals, now);
        if (!applied.Changes.IsEmpty)
            bus.Publish(applied.Changes.Build(now, state.Snapshot));
        applied.Complete();
    }

    // Memory must not keep what the database rolled back: read it all again. If even that fails, the store is unusable and
    // every later command fails with the same cause.
    private async Task ReloadAsync()
    {
        try
        {
            state = await WriterState.LoadAsync(database.Database, files, CancellationToken.None).ConfigureAwait(false);
            Volatile.Write(ref trust, state.Snapshot);
        }
        catch (Exception e)
        {
            fault = e;
        }
    }

    private static void Fail(List<StoreCommand> batch, Exception error)
    {
        foreach (StoreCommand command in batch)
            command.Fail(error);
    }
}
