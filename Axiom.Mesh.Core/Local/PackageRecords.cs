using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Local;
using LocalPackageState = Axiom.Mesh.Local.PackageState;

namespace Axiom.Mesh.Core;

/// <summary>
/// The <c>Package</c> record of LOCAL-SPEC.md §5.4 from what the node holds: the current Manifest and Grant from trust,
/// the Heads from the writer's view, the count of stored Entries from the index, and the established peers that list it
/// from replication. One builder serves <c>GetPackage</c>, <c>ListPackages</c>, the package requests and the
/// <c>PackageChanged</c> events, so every one of them shows the same record.
/// </summary>
internal static class PackageRecords
{
    /// <summary>The record of a Package as it stands now.</summary>
    /// <param name="node">The node: trust, Heads, the index, the sessions.</param>
    /// <param name="package">The Package.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The record; a Package the node no longer holds comes back Listed with its id only.</returns>
    public static async Task<Package> BuildAsync(MeshNode node, PackageId package, CancellationToken cancellationToken)
    {
        StoreWriter writer = node.Writer;
        ulong entries = await CountAsync(writer, package, cancellationToken).ConfigureAwait(false);
        return Of(package, writer.Trust, writer.Heads.Of(package), entries, node.Replicating.ListedBy(package).Count);
    }

    /// <summary>The record of a Package from the facts it is made of.</summary>
    /// <param name="package">The Package.</param>
    /// <param name="trust">The node's trust: state, Manifest, Grants, policy, the own user.</param>
    /// <param name="heads">The Heads this node stored for it, any order.</param>
    /// <param name="entries">How many of its Entries are stored.</param>
    /// <param name="listedBy">How many established peers list it now.</param>
    /// <returns>The record; a Package the node does not hold comes back Listed with its id, Heads and count only.</returns>
    public static Package Of(PackageId package, TrustSnapshot trust, IReadOnlyList<Head> heads, ulong entries, int listedBy)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(heads);
        Head[] ordered = [.. heads.OrderBy(h => h.Device.Value).Take(Limits.MaxPackageHeads)];
        ulong latest = heads.Count == 0 ? 0 : heads.Max(h => h.Clock);
        ushort listing = (ushort)Math.Clamp(listedBy, 0, ushort.MaxValue);
        if (!trust.TryGetPackage(package, out PackageTrust? held))
            return new Package { PackageId = package, State = LocalPackageState.Listed, Heads = ordered, Entries = entries, LatestClock = latest, ListedBy = listing };

        ManifestContent? manifest = held.Manifest?.Content;
        return new Package
        {
            PackageId = package,
            State = held.IsFollowed ? LocalPackageState.Followed : LocalPackageState.Listed,
            OwnerId = manifest?.Owner ?? default,
            Kind = manifest?.Kind ?? default,
            Name = manifest?.Name ?? string.Empty,
            Created = manifest?.Created ?? 0,
            Retention = manifest?.Retention ?? default,
            ManifestSeq = manifest?.Seq ?? 0,
            ManifestHash = manifest?.Hash ?? default,
            Branches = manifest?.Branches ?? [],
            ListedBy = listing,
            Policy = (LocalPolicy)(byte)held.Policy,
            Rights = RightsOf(trust, held),
            Heads = ordered,
            Entries = entries,
            LatestClock = latest,
        };
    }

    // §5.4: Write for the owner, else the current Grant's rights, else none.
    private static GrantRights RightsOf(TrustSnapshot trust, PackageTrust package)
    {
        if (trust.Self is not { } self || package.Manifest is not { } manifest)
            return GrantRights.None;
        if (manifest.Content.Owner == self.User)
            return GrantRights.Write;
        return package.Grants.TryGetValue(self.User, out VerifiedGrant? grant) && package.WithdrawnGrant != grant.Hash ? grant.Content.Rights : GrantRights.None;
    }

    private static async Task<ulong> CountAsync(StoreWriter writer, PackageId package, CancellationToken cancellationToken)
    {
        await using LdbContext context = await LdbContext.OpenAsync(writer.Database, cancellationToken).ConfigureAwait(false);
        long count = await context.Set<EntryRow>().ReadOnlyQuery()
            .Where(EntryRow.Columns.Package == RowKeys.Of(package.Value))
            .CountAsync(cancellationToken).ConfigureAwait(false);
        return (ulong)Math.Max(0, count);
    }
}
