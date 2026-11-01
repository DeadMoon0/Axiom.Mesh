using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;
using Axiom.LDB.Query;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// One entity table inside the writer's transaction: typed reads that see the transaction's own writes, whole-row
/// upserts by key, and deletes by key. The store writer works on these rather than on an <see cref="LdbContext"/>,
/// whose reads see only the snapshot it opened at.
/// </summary>
/// <typeparam name="T">The entity class.</typeparam>
internal sealed class Rows<T>
    where T : class, IEntity<T>
{
    private readonly TableWriter table;
    private readonly string keyColumn;
    private readonly string keyIndex;

    /// <summary>The entity's table in <paramref name="transaction"/>.</summary>
    /// <param name="transaction">A write transaction that holds the table.</param>
    public Rows(WriteTransaction transaction)
    {
        table = transaction.Table(T.Model.Schema.Name);
        keyColumn = T.Model.Schema.Columns[SingleKeyColumn()].Name;
        keyIndex = T.Model.Schema.Indexes.First(i => i.Unique && i.Columns.Count == 1 && i.Columns[0] == keyColumn).Name;
    }

    /// <summary>The table's name.</summary>
    public static string Table => T.Model.Schema.Name;

    /// <summary>
    /// The position of the entity's one key column. Every entity of the store keys on one column (the concatenated keys of
    /// STRUCTURAL-DEBT.md 15); a composite key would need the key's every column in the lookups below.
    /// </summary>
    private static int SingleKeyColumn() =>
        T.Model.Key.Count == 1
            ? T.Model.Key[0]
            : throw new System.InvalidOperationException($"{T.Model.Schema.Name} keys on {T.Model.Key.Count} columns; the store writer handles one.");

    /// <summary>A query over the table as the transaction sees it, own writes included.</summary>
    /// <returns>The query.</returns>
    public IQuery<T> Query() => table.Query(T.Model.Read, T.Model.ColumnOf);

    /// <summary>The row with a key, as the transaction sees it.</summary>
    /// <param name="key">The key's value.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The row, or <see langword="null"/>.</returns>
    public Task<T?> FindAsync(object key, CancellationToken cancellationToken) =>
        Query().Where(Predicate.Equal(keyColumn, key)).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Inserts the row, or replaces every column of the row with its key.</summary>
    /// <param name="row">The row.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>A task that completes when the row is written.</returns>
    public async Task PutAsync(T row, CancellationToken cancellationToken) =>
        await table.UpsertAsync(keyIndex, Row.Of(T.Model.Values(row)), cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes the row with a key, when there is one.</summary>
    /// <param name="key">The key's value.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns><see langword="true"/> when a row was deleted.</returns>
    public async Task<bool> DeleteAsync(object key, CancellationToken cancellationToken) =>
        await table.DeleteWhereAsync(Predicate.Equal(keyColumn, key), cancellationToken).ConfigureAwait(false) > 0;

    /// <summary>Deletes every row that matches.</summary>
    /// <param name="where">The condition.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>How many rows were deleted.</returns>
    public async Task<long> DeleteWhereAsync(Predicate where, CancellationToken cancellationToken) =>
        await table.DeleteWhereAsync(where, cancellationToken).ConfigureAwait(false);
}
