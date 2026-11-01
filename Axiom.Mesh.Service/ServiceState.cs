using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;

namespace Axiom.Mesh.Service;

/// <summary>
/// A service's state (DESIGN.md §13, S-D2): one LDB database under its state directory, shared by the service's own rows
/// and the runtime's <see cref="IndexCursor"/>. The service opens its tables on <see cref="Database"/>; the runtime advances
/// the cursor in a transaction of its own after each Entry's handler returned, lazily durable (LDB <c>Durability.Lazy</c>: a
/// crash loses only the latest commits, in order), so a crash repeats an Entry and never skips
/// one: a handler dedupes by the Entry's hash.
/// </summary>
public sealed class ServiceState
{
    private const string CursorName = "cursor";

    private ServiceState(Database database) => Database = database;

    /// <summary>The service's database: create its tables here (<c>LdbContext.EnsureTableAsync</c>).</summary>
    public Database Database { get; }

    /// <summary>How far the service has indexed.</summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The cursor; <see cref="IndexCursor.None"/> before the first Entry.</returns>
    public async Task<IndexCursor> ReadCursorAsync(CancellationToken cancellationToken)
    {
        await using LdbContext context = await LdbContext.OpenAsync(Database, cancellationToken).ConfigureAwait(false);
        CursorRow? row = await context.Set<CursorRow>().FindAsync(CursorName, cancellationToken).ConfigureAwait(false);
        return row is null
            ? IndexCursor.None
            : new IndexCursor { Epoch = row.Epoch is { Length: Hash32.Length } epoch ? new Hash32(epoch) : default, LastSeq = row.LastSeq };
    }

    /// <summary>Records that every Entry up to <paramref name="lastSeq"/> of <paramref name="epoch"/> was handled.</summary>
    /// <param name="epoch">The store epoch.</param>
    /// <param name="lastSeq">The store sequence of the Entry just handled.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is committed.</returns>
    public Task AdvanceAsync(Hash32 epoch, ulong lastSeq, CancellationToken cancellationToken) => WriteAsync(epoch, lastSeq, Durability.Lazy, cancellationToken);

    /// <summary>Starts the cursor over under a new store epoch (LOCAL-SPEC.md §9.3): nothing of it is indexed yet.</summary>
    /// <param name="epoch">The Host's store epoch.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is committed.</returns>
    public Task DiscardAsync(Hash32 epoch, CancellationToken cancellationToken) => WriteAsync(epoch, 0, Durability.Full, cancellationToken);

    /// <inheritdoc />
    public override string ToString() => $"service state on {Database}";

    /// <summary>The state of a database, the cursor's table ensured.</summary>
    /// <param name="database">The service's database.</param>
    /// <param name="cancellationToken">Cancels opening.</param>
    /// <returns>The state.</returns>
    internal static async Task<ServiceState> OpenAsync(Database database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        await using (LdbContext context = await LdbContext.OpenAsync(database, cancellationToken).ConfigureAwait(false))
            await context.EnsureTableAsync<CursorRow>(cancellationToken).ConfigureAwait(false);
        return new ServiceState(database);
    }

    private async Task WriteAsync(Hash32 epoch, ulong lastSeq, Durability durability, CancellationToken cancellationToken)
    {
        await using LdbContext context = await LdbContext.OpenAsync(Database, cancellationToken).ConfigureAwait(false);
        CursorRow? row = await context.Set<CursorRow>().FindAsync(CursorName, cancellationToken).ConfigureAwait(false);
        byte[] bytes = new byte[Hash32.Length];
        epoch.CopyTo(bytes);
        if (row is null)
        {
            context.Set<CursorRow>().Add(new CursorRow { Name = CursorName, Epoch = bytes, LastSeq = lastSeq });
        }
        else
        {
            row.Epoch = bytes;
            row.LastSeq = lastSeq;
        }

        await context.SaveChangesAsync(TransactionOptions.Default with { Durability = durability }, cancellationToken).ConfigureAwait(false);
    }
}
