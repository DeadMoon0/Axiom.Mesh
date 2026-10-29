using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// What the store writer keeps in memory between batches: the counters the <c>Meta</c> rows persist and the working
/// copy of trust. Loaded from the database when the writer starts, and again after a batch failed to commit, so memory
/// never holds a change the database does not.
/// </summary>
internal sealed class WriterState
{
    private WriterState(TrustBuilder trust, TrustSnapshot snapshot, Dictionary<DeviceId, PublicKey> keys)
    {
        Trust = trust;
        Snapshot = snapshot;
        Keys = keys;
    }

    /// <summary>The last store sequence assigned.</summary>
    public ulong StoreSeq { get; set; }

    /// <summary>Stored objects.</summary>
    public long Objects { get; set; }

    /// <summary>Bytes of stored objects.</summary>
    public long Bytes { get; set; }

    /// <summary>Bytes of objects with at least one pin.</summary>
    public long PinnedBytes { get; set; }

    /// <summary>The working copy of trust.</summary>
    public TrustBuilder Trust { get; set; }

    /// <summary>The trust last swapped in.</summary>
    public TrustSnapshot Snapshot { get; set; }

    /// <summary>One key instance per device, across snapshots.</summary>
    public Dictionary<DeviceId, PublicKey> Keys { get; }

    /// <summary>The stored Heads; a batch replaces the view, the writer publishes it once the batch committed.</summary>
    public HeadsView Heads { get; set; } = HeadsView.Empty;

    /// <summary>Reads the counters and trust the database holds.</summary>
    /// <param name="database">The database.</param>
    /// <param name="files">The object files the current Rosters, Manifests and Grants are read from.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The state.</returns>
    /// <exception cref="MeshStoreException">A current object's file is missing or damaged.</exception>
    public static async Task<WriterState> LoadAsync(Database database, IObjectFiles files, CancellationToken cancellationToken)
    {
        await using LdbContext context = await LdbContext.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        Dictionary<string, MetaRow> meta = (await context.Set<MetaRow>().ReadOnlyQuery().ToListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.Name);
        var keys = new Dictionary<DeviceId, PublicKey>();
        var trust = new TrustBuilder(TrustSnapshot.Empty, keys);

        List<DeviceRow> devices = await context.Set<DeviceRow>().ReadOnlyQuery().ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (UserRow row in await context.Set<UserRow>().ReadOnlyQuery().ToListAsync(cancellationToken).ConfigureAwait(false))
            trust.Restore(await UserAsync(row, devices, files, cancellationToken).ConfigureAwait(false));

        List<GrantRow> grants = await context.Set<GrantRow>().ReadOnlyQuery().ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (PackageRow row in await context.Set<PackageRow>().ReadOnlyQuery().ToListAsync(cancellationToken).ConfigureAwait(false))
            trust.SetPackage(await PackageAsync(row, grants, files, cancellationToken).ConfigureAwait(false));

        if (meta.TryGetValue(MetaKeys.SelfUserKey, out MetaRow? user) && meta.TryGetValue(MetaKeys.SelfDevice, out MetaRow? device)
            && PublicKey.TryParse(user.Bytes, out PublicKey? userKey))
            trust.SetSelf(new SelfIdentity(userKey, new DeviceId(RowKeys.Hash(device.Bytes!)))
            {
                UserName = meta.TryGetValue(MetaKeys.SelfName, out var name) && name.Bytes is { } utf8 ? Encoding.UTF8.GetString(utf8) : string.Empty,
                CreatedMs = (long)Number(meta, MetaKeys.SelfCreated),
            });

        List<HeadRow> heads = await context.Set<HeadRow>().ReadOnlyQuery().ToListAsync(cancellationToken).ConfigureAwait(false);
        return new WriterState(trust, trust.Build(), keys)
        {
            Heads = HeadsView.Of(heads.Select(h => new KeyValuePair<PackageId, Head>(
                new PackageId(RowKeys.Hash(h.Package)),
                new Head(new DeviceId(RowKeys.Hash(h.Device)), new ObjectHash(RowKeys.Hash(h.Entry)), h.Clock)))),
            StoreSeq = Number(meta, MetaKeys.StoreSeq),
            Objects = (long)Number(meta, MetaKeys.StoreObjects),
            Bytes = (long)Number(meta, MetaKeys.StoreBytes),
            PinnedBytes = (long)Number(meta, MetaKeys.StorePinnedBytes),
        };
    }

    private static ulong Number(Dictionary<string, MetaRow> meta, string name) => meta.TryGetValue(name, out MetaRow? row) ? row.Number : 0;

    private static async Task<UserTrust> UserAsync(UserRow row, List<DeviceRow> devices, IObjectFiles files, CancellationToken cancellationToken)
    {
        if (!PublicKey.TryParse(row.UserKey, out PublicKey? key))
            throw new MeshStoreException($"The node's index holds a user key that is not a P-256 point (user {RowKeys.Hash(row.User).ToHex()}).", "Restore the data directory from a backup taken while the node was stopped.");

        VerifiedRoster? roster = row.Roster is null ? null : await StoredObjects.RosterAsync(files, new ObjectHash(RowKeys.Hash(row.Roster)), cancellationToken).ConfigureAwait(false);
        Dictionary<DeviceId, DeviceRow> own = devices.Where(d => d.User.AsSpan().SequenceEqual(row.User)).ToDictionary(d => new DeviceId(RowKeys.Hash(d.Device)));
        EffectiveDevice[] effective = roster is null
            ? []
            : [.. roster.Content.Devices.Select(d => new EffectiveDevice(d, own.TryGetValue(d.Id, out DeviceRow? stored) ? stored.Removal : DeviceRemoval.None))];
        HashSet<DeviceId> removedEver = [.. own.Where(d => d.Value.RemovedEver).Select(d => d.Key)];
        return new UserTrust(key, row.Paired, roster, effective, removedEver);
    }

    private static async Task<PackageTrust> PackageAsync(PackageRow row, List<GrantRow> grants, IObjectFiles files, CancellationToken cancellationToken)
    {
        var id = new PackageId(RowKeys.Hash(row.Package));
        VerifiedManifest? manifest = row.Manifest is null ? null : await StoredObjects.ManifestAsync(files, new ObjectHash(RowKeys.Hash(row.Manifest)), cancellationToken).ConfigureAwait(false);
        var current = new Dictionary<UserId, VerifiedGrant>();
        if (manifest is not null)
        {
            foreach (GrantRow grant in grants.Where(g => g.Package.AsSpan().SequenceEqual(row.Package)))
                current[new UserId(RowKeys.Hash(grant.Grantee))] = await StoredObjects.GrantAsync(files, new ObjectHash(RowKeys.Hash(grant.Grant)), manifest, cancellationToken).ConfigureAwait(false);
        }

        return new PackageTrust(id, row.State, manifest, current, row.Policy);
    }
}
