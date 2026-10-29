using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// The node's database (DESIGN.md §13), its tables ensured: opened on a directory or on a <see cref="LDB.Database"/>
/// the caller made (an in-memory one in tests). The first open creates the store epoch (LOCAL-SPEC.md §9.3) together
/// with the <c>Meta</c> table, in one commit, so it exists exactly once and never changes; a database made by another
/// schema version is refused with the steps out.
/// </summary>
internal sealed class MeshDatabase : IAsyncDisposable
{
    /// <summary>The schema version these tables are; a database of another one is refused.</summary>
    public const ulong SchemaVersion = 1;

    private readonly bool ownsDatabase;

    private MeshDatabase(Database database, bool ownsDatabase, Hash32 epoch)
    {
        Database = database;
        this.ownsDatabase = ownsDatabase;
        Epoch = epoch;
    }

    /// <summary>The database.</summary>
    public Database Database { get; }

    /// <summary>The store epoch: made once with the database, never changed.</summary>
    public Hash32 Epoch { get; }

    /// <summary>Opens (or creates) the database in a directory; the result owns it.</summary>
    /// <param name="directory">The database's directory.</param>
    /// <param name="options">How the connection behaves; its <c>TimeProvider</c> should be the node's.</param>
    /// <param name="entropy">Randomness for the store epoch of a new database.</param>
    /// <param name="cancellationToken">Cancels opening.</param>
    /// <returns>The database, its tables ensured.</returns>
    /// <exception cref="MeshStoreException">The database was made by another schema version.</exception>
    public static async Task<MeshDatabase> OpenAsync(string directory, DatabaseOptions options, IEntropy entropy, CancellationToken cancellationToken)
    {
        Database database = await Database.OpenAsync(directory, options, cancellationToken).ConfigureAwait(false);
        try
        {
            return await EnsureAsync(database, ownsDatabase: true, entropy, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await database.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Ensures the tables of a database the caller opened and keeps; disposing the result leaves it open.</summary>
    /// <param name="database">The database.</param>
    /// <param name="entropy">Randomness for the store epoch of a new database.</param>
    /// <param name="cancellationToken">Cancels opening.</param>
    /// <returns>The database, its tables ensured.</returns>
    /// <exception cref="MeshStoreException">The database was made by another schema version.</exception>
    public static Task<MeshDatabase> OpenAsync(Database database, IEntropy entropy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        return EnsureAsync(database, ownsDatabase: false, entropy, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (ownsDatabase)
            await Database.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task<MeshDatabase> EnsureAsync(Database database, bool ownsDatabase, IEntropy entropy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entropy);
        await using LdbContext context = await LdbContext.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        try
        {
            await context.EnsureTableAsync(() => FirstMeta(entropy), cancellationToken).ConfigureAwait(false);
            MetaRow? version = await context.Set<MetaRow>().FindAsync(MetaKeys.SchemaVersion, cancellationToken).ConfigureAwait(false);
            if (version is null || version.Number != SchemaVersion)
                throw Mismatch($"schema version {version?.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}", null);

            await context.EnsureTableAsync<UserRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<DeviceRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<PackageRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<GrantRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<HeadRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<EntryRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<ObjectRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<PinRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<PeerRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<ReferenceRow>(cancellationToken).ConfigureAwait(false);

            MetaRow epoch = await context.Set<MetaRow>().FindAsync(MetaKeys.StoreEpoch, cancellationToken).ConfigureAwait(false)
                ?? throw Mismatch("no store epoch", null);
            return new MeshDatabase(database, ownsDatabase, new Hash32(epoch.Bytes));
        }
        catch (SchemaException e)
        {
            throw Mismatch("a table of another shape", e);
        }
    }

    private static IEnumerable<MetaRow> FirstMeta(IEntropy entropy)
    {
        byte[] epoch = new byte[Hash32.Length];
        entropy.Fill(epoch);
        return
        [
            new MetaRow { Name = MetaKeys.SchemaVersion, Number = SchemaVersion },
            new MetaRow { Name = MetaKeys.StoreEpoch, Bytes = epoch },
            new MetaRow { Name = MetaKeys.StoreSeq },
            new MetaRow { Name = MetaKeys.StoreObjects },
            new MetaRow { Name = MetaKeys.StoreBytes },
            new MetaRow { Name = MetaKeys.StorePinnedBytes },
        ];
    }

    private static MeshStoreException Mismatch(string found, Exception? cause)
    {
        string message = $"The node's database was not made by this version of Axiom.Mesh (schema version {SchemaVersion} expected, found {found}).";
        string[] steps =
        [
            "Run the version of Axiom.Mesh that made this database, or a later one that can migrate it.",
            "Or move the data directory aside and start anew: the node then fetches what it follows from its peers, and clients see a new store epoch.",
        ];
        return cause is null
            ? new MeshStoreException(message, steps[0], steps[1])
            : new MeshStoreException(message, cause, steps[0], steps[1]);
    }
}
