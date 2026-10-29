using System;
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
/// the Heads from the writer's view, the count of stored Entries from the index.
/// </summary>
internal static class PackageRecords
{
    /// <summary>The record of a Package as it stands now.</summary>
    /// <param name="writer">The store writer: trust, Heads, the index.</param>
    /// <param name="package">The Package.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The record; a Package the node no longer holds comes back Listed with its id only.</returns>
    public static async Task<Package> BuildAsync(StoreWriter writer, PackageId package, CancellationToken cancellationToken)
    {
        TrustSnapshot trust = writer.Trust;
        Head[] heads = [.. writer.Heads.Of(package).OrderBy(h => h.Device.Value)];
        ulong entries = await CountAsync(writer, package, cancellationToken).ConfigureAwait(false);
        if (!trust.TryGetPackage(package, out PackageTrust? held))
            return new Package { PackageId = package, State = LocalPackageState.Listed, Heads = heads, Entries = entries };

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
            Policy = (LocalPolicy)(byte)held.Policy,
            Rights = RightsOf(trust, held),
            Heads = heads,
            Entries = entries,
            LatestClock = heads.Length == 0 ? 0 : heads.Max(h => h.Clock),
        };
    }

    // §5.4: Write for the owner, else the current Grant's rights, else none.
    private static GrantRights RightsOf(TrustSnapshot trust, PackageTrust package)
    {
        if (trust.Self is not { } self || package.Manifest is not { } manifest)
            return GrantRights.None;
        if (manifest.Content.Owner == self.User)
            return GrantRights.Write;
        return package.Grants.TryGetValue(self.User, out VerifiedGrant? grant) ? grant.Content.Rights : GrantRights.None;
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
