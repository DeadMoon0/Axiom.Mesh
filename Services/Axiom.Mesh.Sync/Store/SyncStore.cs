using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The sync service's database (SYNC-SERVICE.md §10, S-D2): bindings, fingerprints, the Version DAG, conflicts and the objects
/// whose pins it still holds, in the service's own LDB database (<c>session.State.Database</c>, <c>&lt;home&gt;/services/sync/index</c>)
/// or any other, in memory for a test. One operation at a time; each write is one transaction. A binding's base and pending
/// Tree are written durably; Versions, fingerprints and puts lazily (a crash loses only the latest, in order, and the next run
/// finds them again).
/// </summary>
internal sealed class SyncStore
{
    /// <summary>The schema the rows are written with.</summary>
    public const int SchemaVersion = 1;

    private const string MetaName = "meta";

    private readonly Database database;
    private readonly SemaphoreSlim gate = new(1, 1);

    private SyncStore(Database database) => this.database = database;

    /// <summary>Ensures the tables on a database and returns the store over it.</summary>
    /// <param name="database">The database; the caller owns and closes it.</param>
    /// <param name="cancellationToken">Cancels opening.</param>
    /// <returns>The store.</returns>
    public static async Task<SyncStore> OpenAsync(Database database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        await using (LdbContext context = await LdbContext.OpenAsync(database, cancellationToken).ConfigureAwait(false))
        {
            await context.EnsureTableAsync<SyncMetaRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<BindingRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<FingerprintRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<VersionRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<ConflictRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<PutRow>(cancellationToken).ConfigureAwait(false);
        }

        return new SyncStore(database);
    }

    /// <summary>Opens a database in a directory, for a benchmark or a tool; the caller closes it.</summary>
    /// <param name="directory">The directory; created when absent.</param>
    /// <param name="cancellationToken">Cancels opening.</param>
    /// <returns>The database.</returns>
    public static Task<Database> OpenDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory.CreateDirectory(directory);
        return Database.OpenAsync(directory, DatabaseOptions.Default, cancellationToken);
    }

    /// <summary>What the meta row says: schema 0 when there is none.</summary>
    public Task<(int Schema, Hash32 Epoch, ulong LastSeq)> ReadMetaAsync(CancellationToken cancellationToken) => ReadAsync(async context =>
    {
        SyncMetaRow? row = await context.Set<SyncMetaRow>().FindAsync(MetaName, cancellationToken).ConfigureAwait(false);
        return row is null ? (0, default(Hash32), 0ul) : (row.SchemaVersion, RowBytes.Hash(row.StoreEpoch), row.LastSeq);
    }, cancellationToken);

    /// <summary>Records the epoch the Version DAG is built under, keeping every row.</summary>
    public Task SetEpochAsync(Hash32 epoch, CancellationToken cancellationToken) => WriteAsync(async context =>
    {
        SyncMetaRow meta = await MetaAsync(context, cancellationToken).ConfigureAwait(false);
        meta.SchemaVersion = SchemaVersion;
        meta.StoreEpoch = RowBytes.Of(epoch);
        return true;
    }, Durability.Full, cancellationToken);

    /// <summary>
    /// Another store epoch (LOCAL-SPEC.md §9.3): the Version DAG is dropped and every binding forgets its leaves and its pending
    /// Tree; its base Tree and fingerprints stay, since they describe the disk, so its next run is a first sync.
    /// </summary>
    public Task DiscardVersionsAsync(Hash32 epoch, CancellationToken cancellationToken) => WriteAsync(async context =>
    {
        foreach (VersionRow row in await context.Set<VersionRow>().Query().ToListAsync(cancellationToken).ConfigureAwait(false))
            context.Set<VersionRow>().Remove(row);
        foreach (BindingRow binding in await context.Set<BindingRow>().Query().ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            binding.BaseLeaves = [];
            binding.PendingTree = null;
            binding.PendingLeaves = [];
        }

        SyncMetaRow meta = await MetaAsync(context, cancellationToken).ConfigureAwait(false);
        meta.SchemaVersion = SchemaVersion;
        meta.StoreEpoch = RowBytes.Of(epoch);
        meta.LastSeq = 0;
        return true;
    }, Durability.Full, cancellationToken);

    /// <summary>Every binding.</summary>
    public Task<List<BindingRow>> BindingsAsync(CancellationToken cancellationToken) =>
        ReadAsync(context => context.Set<BindingRow>().ReadOnlyQuery().ToListAsync(cancellationToken), cancellationToken);

    /// <summary>Writes a binding, durably: its base, its pending Tree, what its last run found.</summary>
    public Task SaveBindingAsync(BindingRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        return WriteAsync(async context =>
        {
            EntitySet<BindingRow> set = context.Set<BindingRow>();
            BindingRow? existing = await set.FindAsync(row.Id, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                set.Add(Copy(row, new BindingRow { Id = row.Id }));
                return true;
            }

            Copy(row, existing);
            return true;
        }, Durability.Full, cancellationToken);
    }

    /// <summary>Removes a binding with its fingerprints, conflicts and put rows.</summary>
    public Task DeleteBindingAsync(byte[] id, CancellationToken cancellationToken) => WriteAsync(async context =>
    {
        if (await context.Set<BindingRow>().FindAsync(id, cancellationToken).ConfigureAwait(false) is { } row)
            context.Set<BindingRow>().Remove(row);
        foreach (FingerprintRow print in await context.Set<FingerprintRow>().Query().Where(FingerprintRow.Columns.Binding == id).ToListAsync(cancellationToken).ConfigureAwait(false))
            context.Set<FingerprintRow>().Remove(print);
        foreach (ConflictRow conflict in await context.Set<ConflictRow>().Query().Where(ConflictRow.Columns.Binding == id).ToListAsync(cancellationToken).ConfigureAwait(false))
            context.Set<ConflictRow>().Remove(conflict);
        foreach (PutRow put in await context.Set<PutRow>().Query().Where(PutRow.Columns.Binding == id).ToListAsync(cancellationToken).ConfigureAwait(false))
            context.Set<PutRow>().Remove(put);
        return true;
    }, Durability.Full, cancellationToken);

    /// <summary>The fingerprints of a binding, by path.</summary>
    public Task<Dictionary<string, Fingerprint>> FingerprintsAsync(byte[] id, CancellationToken cancellationToken) => ReadAsync(async context =>
    {
        List<FingerprintRow> rows = await context.Set<FingerprintRow>().ReadOnlyQuery().Where(FingerprintRow.Columns.Binding == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var prints = new Dictionary<string, Fingerprint>(rows.Count, StringComparer.Ordinal);
        foreach (FingerprintRow row in rows)
            prints[row.Path] = new Fingerprint(row.Path, (TreeEntryMode)row.Mode, row.Size, row.WriteTicks, new ObjectHash(RowBytes.Hash(row.Hash)), row.TakenTicks);
        return prints;
    }, cancellationToken);

    /// <summary>Writes and removes fingerprints of a binding in one transaction.</summary>
    public Task WriteFingerprintsAsync(byte[] id, IReadOnlyCollection<Fingerprint> upserts, IReadOnlyCollection<string> deletes, Durability durability, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upserts);
        ArgumentNullException.ThrowIfNull(deletes);
        if (upserts.Count == 0 && deletes.Count == 0)
            return Task.CompletedTask;
        return WriteAsync(async context =>
        {
            EntitySet<FingerprintRow> set = context.Set<FingerprintRow>();
            foreach (string path in deletes)
            {
                if (await set.FindAsync(RowBytes.PathKey(id, path), cancellationToken).ConfigureAwait(false) is { } gone)
                    set.Remove(gone);
            }

            foreach (Fingerprint print in upserts)
            {
                byte[] key = RowBytes.PathKey(id, print.Path);
                FingerprintRow? row = await set.FindAsync(key, cancellationToken).ConfigureAwait(false);
                if (row is null)
                {
                    row = new FingerprintRow { Key = key, Binding = id, Path = print.Path };
                    set.Add(row);
                }

                row.Mode = (byte)print.Mode;
                row.Size = print.Size;
                row.WriteTicks = print.WriteTicks;
                row.TakenTicks = print.TakenTicks;
                row.Hash = RowBytes.Of(print.Hash.Value);
            }

            return true;
        }, durability, cancellationToken);
    }

    /// <summary>Every Version of every Archive.</summary>
    public Task<List<VersionRow>> VersionsAsync(CancellationToken cancellationToken) =>
        ReadAsync(context => context.Set<VersionRow>().ReadOnlyQuery().ToListAsync(cancellationToken), cancellationToken);

    /// <summary>Adds Versions not recorded yet, and moves the stream's position to <paramref name="lastSeq"/> when given, in one transaction.</summary>
    public Task AddVersionsAsync(IReadOnlyList<VersionRow> rows, ulong? lastSeq, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return WriteAsync(async context =>
        {
            EntitySet<VersionRow> set = context.Set<VersionRow>();
            foreach (VersionRow row in rows)
            {
                VersionRow? existing = await set.FindAsync(row.Hash, cancellationToken).ConfigureAwait(false);
                if (existing is null)
                    set.Add(row);
                else if (existing.StoreSeq == 0 && row.StoreSeq != 0)
                    existing.StoreSeq = row.StoreSeq;
            }

            if (lastSeq is { } seq)
            {
                SyncMetaRow meta = await MetaAsync(context, cancellationToken).ConfigureAwait(false);
                meta.LastSeq = Math.Max(meta.LastSeq, seq);
            }

            return true;
        }, Durability.Lazy, cancellationToken);
    }

    /// <summary>Records conflicts, numbering them in order; returns them with their numbers.</summary>
    public Task<IReadOnlyList<ConflictRow>> AddConflictsAsync(IReadOnlyList<ConflictRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return WriteAsync<IReadOnlyList<ConflictRow>>(async context =>
        {
            SyncMetaRow meta = await MetaAsync(context, cancellationToken).ConfigureAwait(false);
            foreach (ConflictRow row in rows)
            {
                row.Id = ++meta.NextConflict;
                row.Key = RowBytes.Number(row.Id);
                context.Set<ConflictRow>().Add(row);
            }

            return rows;
        }, Durability.Full, cancellationToken);
    }

    /// <summary>A page of conflicts after a number, of one binding or of all.</summary>
    public Task<(List<ConflictRow> Rows, bool More)> ConflictsAsync(byte[]? binding, ulong after, int limit, CancellationToken cancellationToken) => ReadAsync(async context =>
    {
        List<ConflictRow> rows = await context.Set<ConflictRow>().ReadOnlyQuery()
            .Where(ConflictRow.Columns.Id > after)
            .OrderBy(ConflictRow.Columns.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        List<ConflictRow> mine = [.. rows.Where(r => binding is null || r.Binding.AsSpan().SequenceEqual(binding))];
        return (mine.Take(limit).ToList(), mine.Count > limit);
    }, cancellationToken);

    /// <summary>How many conflicts a binding has.</summary>
    public Task<int> ConflictCountAsync(byte[] binding, CancellationToken cancellationToken) => ReadAsync(async context =>
        (await context.Set<ConflictRow>().ReadOnlyQuery().Where(ConflictRow.Columns.Binding == binding).ToListAsync(cancellationToken).ConfigureAwait(false)).Count,
        cancellationToken);

    /// <summary>Records objects a run put, whose pins it holds until they are released.</summary>
    public Task AddPutsAsync(byte[] binding, IReadOnlyCollection<ObjectHash> hashes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.Count == 0)
            return Task.CompletedTask;
        return WriteAsync(async context =>
        {
            // The rows a binding holds are few (a run releases them at its end): read them once, then only add.
            EntitySet<PutRow> set = context.Set<PutRow>();
            HashSet<ObjectHash> held = [.. (await set.ReadOnlyQuery().Where(PutRow.Columns.Binding == binding).ToListAsync(cancellationToken).ConfigureAwait(false))
                .Select(r => new ObjectHash(RowBytes.Hash(r.Hash)))];
            foreach (ObjectHash hash in hashes)
            {
                if (!held.Add(hash))
                    continue;
                byte[] value = RowBytes.Of(hash.Value);
                set.Add(new PutRow { Key = RowBytes.Keyed(binding, value), Binding = binding, Hash = value });
            }

            return true;
        }, Durability.Lazy, cancellationToken);
    }

    /// <summary>The objects whose pins a binding still holds.</summary>
    public Task<IReadOnlyList<ObjectHash>> PutsAsync(byte[] binding, CancellationToken cancellationToken) => ReadAsync<IReadOnlyList<ObjectHash>>(async context =>
        [.. (await context.Set<PutRow>().ReadOnlyQuery().Where(PutRow.Columns.Binding == binding).ToListAsync(cancellationToken).ConfigureAwait(false)).Select(r => new ObjectHash(RowBytes.Hash(r.Hash)))],
        cancellationToken);

    /// <summary>Forgets put rows once their pins were released.</summary>
    public Task RemovePutsAsync(byte[] binding, IReadOnlyCollection<ObjectHash> hashes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.Count == 0)
            return Task.CompletedTask;
        return WriteAsync(async context =>
        {
            foreach (ObjectHash hash in hashes)
            {
                if (await context.Set<PutRow>().FindAsync(RowBytes.Keyed(binding, RowBytes.Of(hash.Value)), cancellationToken).ConfigureAwait(false) is { } row)
                    context.Set<PutRow>().Remove(row);
            }

            return true;
        }, Durability.Lazy, cancellationToken);
    }

    /// <inheritdoc />
    public override string ToString() => $"sync store on {database}";

    private static BindingRow Copy(BindingRow from, BindingRow to)
    {
        to.Directory = from.Directory;
        to.Package = from.Package;
        to.Label = from.Label;
        to.Symlinks = from.Symlinks;
        to.Ignore = from.Ignore;
        to.BaseTree = from.BaseTree;
        to.BaseLeaves = from.BaseLeaves;
        to.PendingTree = from.PendingTree;
        to.PendingLeaves = from.PendingLeaves;
        to.State = from.State;
        to.Error = from.Error;
        to.LastRunMs = from.LastRunMs;
        to.Runs = from.Runs;
        to.Files = from.Files;
        to.Bytes = from.Bytes;
        to.LocalChanges = from.LocalChanges;
        return to;
    }

    private static async Task<SyncMetaRow> MetaAsync(LdbContext context, CancellationToken cancellationToken)
    {
        EntitySet<SyncMetaRow> set = context.Set<SyncMetaRow>();
        SyncMetaRow? meta = await set.FindAsync(MetaName, cancellationToken).ConfigureAwait(false);
        if (meta is not null)
            return meta;
        meta = new SyncMetaRow { Name = MetaName, SchemaVersion = SchemaVersion };
        set.Add(meta);
        return meta;
    }

    private async Task<T> ReadAsync<T>(Func<LdbContext, Task<T>> read, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using LdbContext context = await LdbContext.OpenAsync(database, cancellationToken).ConfigureAwait(false);
            return await read(context).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<T> WriteAsync<T>(Func<LdbContext, Task<T>> write, Durability durability, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using LdbContext context = await LdbContext.OpenAsync(database, cancellationToken).ConfigureAwait(false);
            T result = await write(context).ConfigureAwait(false);
            await context.SaveChangesAsync(TransactionOptions.Default with { Durability = durability }, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
}
