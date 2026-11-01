using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The Version DAG of every Archive the Host holds (SYNC-SERVICE.md §10): kept in memory for the rules and in the database as
/// <see cref="VersionRow"/>s, fed by the runtime's Entry stream in store order and by the Versions this service writes itself.
/// The stream's position moves only with the stream, so a Version written here never hides one the stream has yet to deliver.
/// </summary>
internal sealed class VersionIndex
{
    private readonly SyncStore store;
    private readonly object gate = new();
    private readonly Dictionary<PackageId, Dictionary<ObjectHash, VersionFact>> byPackage = [];
    private ulong lastSeq;

    private VersionIndex(SyncStore store) => this.store = store;

    /// <summary>The store sequence of the last Version the stream delivered.</summary>
    public ulong LastSeq
    {
        get
        {
            lock (gate)
                return lastSeq;
        }
    }

    /// <summary>Loads the DAG from the database.</summary>
    /// <param name="store">The database.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The index.</returns>
    public static async Task<VersionIndex> LoadAsync(SyncStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        var index = new VersionIndex(store);
        (_, _, ulong seq) = await store.ReadMetaAsync(cancellationToken).ConfigureAwait(false);
        index.lastSeq = seq;
        foreach (VersionRow row in await store.VersionsAsync(cancellationToken).ConfigureAwait(false))
            index.Add(new PackageId(RowBytes.Hash(row.Package)), FactOf(row));
        return index;
    }

    /// <summary>Takes in Entries: every Version among them is recorded, once.</summary>
    /// <param name="entries">The Entries.</param>
    /// <param name="fromStream">They came from the runtime's stream in store order: the stream's position moves to the last.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The Archives that got a Version they did not have.</returns>
    public async Task<IReadOnlyList<PackageId>> TakeInAsync(IReadOnlyList<Entry> entries, bool fromStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var rows = new List<VersionRow>();
        var touched = new HashSet<PackageId>();
        foreach (Entry entry in entries)
        {
            if (!VersionFact.TryOf(entry, out VersionFact? fact))
                continue;
            rows.Add(RowOf(entry.PackageId, fact, fromStream ? entry.StoreSeq : 0));
            if (Add(entry.PackageId, fact))
                touched.Add(entry.PackageId);
        }

        ulong? seq = fromStream && entries.Count > 0 ? entries.Max(e => e.StoreSeq) : null;
        if (rows.Count > 0 || seq is not null)
            await store.AddVersionsAsync(rows, seq, cancellationToken).ConfigureAwait(false);
        if (seq is { } moved)
        {
            lock (gate)
                lastSeq = Math.Max(lastSeq, moved);
        }

        return [.. touched];
    }

    /// <summary>The Versions of one Archive, by hash: a snapshot the rules may read while the index grows.</summary>
    /// <param name="package">The Archive.</param>
    /// <returns>The Versions.</returns>
    public IReadOnlyDictionary<ObjectHash, VersionFact> Of(PackageId package)
    {
        lock (gate)
            return byPackage.TryGetValue(package, out Dictionary<ObjectHash, VersionFact>? versions) ? new Dictionary<ObjectHash, VersionFact>(versions) : new Dictionary<ObjectHash, VersionFact>();
    }

    /// <summary>Whether a Version is recorded.</summary>
    /// <param name="hash">The Entry's hash.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Contains(ObjectHash hash)
    {
        lock (gate)
            return byPackage.Values.Any(v => v.ContainsKey(hash));
    }

    /// <summary>Forgets every Version in memory, after the database discarded them (another store epoch).</summary>
    public void Clear()
    {
        lock (gate)
        {
            byPackage.Clear();
            lastSeq = 0;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        lock (gate)
            return $"{byPackage.Values.Sum(v => v.Count)} Versions of {byPackage.Count} Archives, stream at {lastSeq}";
    }

    private bool Add(PackageId package, VersionFact fact)
    {
        lock (gate)
        {
            if (!byPackage.TryGetValue(package, out Dictionary<ObjectHash, VersionFact>? versions))
                byPackage[package] = versions = [];
            return versions.TryAdd(fact.Hash, fact);
        }
    }

    private static VersionFact FactOf(VersionRow row)
    {
        var hash = new ObjectHash(RowBytes.Hash(row.Hash));
        return new VersionFact(hash, new EntryKey(row.Clock, new DeviceId(RowBytes.Hash(row.Device)), hash), RowBytes.Split(row.Parents), row.Label, new ObjectHash(RowBytes.Hash(row.Tree)));
    }

    private static VersionRow RowOf(PackageId package, VersionFact fact, ulong storeSeq) => new()
    {
        Hash = RowBytes.Of(fact.Hash.Value),
        Package = RowBytes.Of(package.Value),
        Clock = fact.Key.Clock,
        Device = RowBytes.Of(fact.Key.Device.Value),
        Parents = RowBytes.Join([.. fact.Parents]),
        Label = fact.Label,
        Tree = RowBytes.Of(fact.Tree.Value),
        StoreSeq = storeSeq,
    };
}
