using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The Package requests of LOCAL-SPEC.md §6.4: <c>ListPackages</c>, <c>GetPackage</c>, <c>CreatePackage</c>,
/// <c>FollowPackage</c>, <c>UnfollowPackage</c>, <c>SetPackagePolicy</c>. <c>UpdatePackage</c> stays unserved (plan D7).
/// Every record comes from <see cref="PackageRecords"/>.
/// </summary>
internal static class PackageRequests
{
    private const int MaxPackages = 256;

    /// <summary><c>ListPackages</c> (304): the Packages this Host knows, Listed and Followed, ascending by id, a page at a time.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> ListPackagesAsync(RequestContext context, uint correlation, ListPackages request, CancellationToken cancellationToken)
    {
        if (!Ascending(request.Kinds.Select(k => (ushort)k)))
            return LocalAnswer.Failure(correlation, Status.Invalid, "The kinds must be ascending and unique.", "kinds");

        TrustSnapshot trust = context.Node.Trust;
        PackageId[] candidates = [.. PackageListing.Select(trust, request.After, request.Kinds, request.OwnerId, request.FollowedOnly)];
        var records = new List<Package>(Math.Min(candidates.Length, MaxPackages + 1));
        foreach (PackageId id in candidates.Take(MaxPackages + 1))
            records.Add(await PackageRecords.BuildAsync(context.Node, id, cancellationToken).ConfigureAwait(false));

        (List<Package> page, bool more) = PageCut.Take(records, MaxPackages, PageCut.FrameBudget, FrameSizes.OfRecord);
        return LocalAnswer.Ok<ListPackages, PackagePage>(correlation, new PackagePage { Packages = page, More = more || candidates.Length > records.Count });
    }

    /// <summary><c>GetPackage</c> (305): one Package; <c>NotFound</c> when the Host knows nothing of it.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> GetPackageAsync(RequestContext context, uint correlation, GetPackage request, CancellationToken cancellationToken)
    {
        if (!context.Node.Trust.IsFollowedOrListed(request.PackageId))
            return Unknown(correlation, request.PackageId);
        return LocalAnswer.Ok<GetPackage, Package>(correlation, await PackageRecords.BuildAsync(context.Node, request.PackageId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// <c>CreatePackage</c> (306): a Manifest with seq 1 for a new Package id (SPEC.md §2.3), owned by the own user,
    /// followed, policy 0. Needs the user key; the Manifest counts against the budget (§6.5).
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> CreatePackageAsync(RequestContext context, uint correlation, CreatePackage request, CancellationToken cancellationToken)
    {
        if (request.Name.Length == 0)
            return LocalAnswer.Failure(correlation, Status.Invalid, "The Package's name must not be empty.", "name");
        if ((byte)request.Retention > (byte)Retention.LatestOnly)
            return LocalAnswer.Failure(correlation, Status.Invalid, $"Retention {(byte)request.Retention} is not 0, 1 or 2.", "retention");

        MeshNode node = context.Node;
        (Status status, VerifiedManifest? manifest) = await node.WithUserKeyAsync<VerifiedManifest>(
            async (signer, ct) =>
            {
                VerifiedManifest signed = new ManifestDraft
                {
                    Nonce = NewNonce(node.Entropy),
                    Kind = request.Kind,
                    Seq = 1,
                    Name = request.Name,
                    Created = (ulong)node.Time.GetUtcNow().ToUnixTimeMilliseconds(),
                    Retention = request.Retention,
                }.Sign(signer);
                if (!StoreBudgetRule.Admits(node.Writer.Totals, signed.Bytes.Length))
                    return (Status.StoreFull, null);
                await node.FollowAsync(signed.Content.Package, ct).ConfigureAwait(false);
                await node.Store.PutAsync(signed, ct).ConfigureAwait(false);
                return (Status.Ok, signed);
            },
            cancellationToken).ConfigureAwait(false);
        return status switch
        {
            Status.Ok => LocalAnswer.Ok<CreatePackage, Package>(correlation, await PackageRecords.BuildAsync(node, manifest!.Content.Package, cancellationToken).ConfigureAwait(false)),
            Status.UserKeyAbsent => LocalAnswer.Failure(correlation, status, "Creating a Package needs the user key, which is on another device of this user: create it there.", string.Empty),
            _ => StoreFull(correlation),
        };
    }

    /// <summary>
    /// <c>FollowPackage</c> (308): a Listed Package becomes Followed and replicates (SPEC.md §10.2); on a Followed one only
    /// the policy is set. <c>NotFound</c> for a Package the Host knows nothing of.
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> FollowPackageAsync(RequestContext context, uint correlation, FollowPackage request, CancellationToken cancellationToken)
    {
        if (!IsPolicy(request.Policy))
            return BadPolicy(correlation, request.Policy);
        MeshNode node = context.Node;
        if (!node.Trust.IsFollowedOrListed(request.PackageId))
            return Unknown(correlation, request.PackageId);

        await node.FollowAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
        await node.Writer.SendAsync(new PackageCommand(PackageCommandKind.SetPolicy, request.PackageId, (LocalRetention)(byte)request.Policy), cancellationToken).ConfigureAwait(false);
        return LocalAnswer.Ok<FollowPackage, Package>(correlation, await PackageRecords.BuildAsync(node, request.PackageId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// <c>UnfollowPackage</c> (309): the Package's <c>Head</c> and <c>Policy</c> pins released, the record Listed again.
    /// <c>Invalid</c> for a Package the own user owns; <c>NotFound</c> for one the Host knows nothing of.
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> UnfollowPackageAsync(RequestContext context, uint correlation, UnfollowPackage request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        TrustSnapshot trust = node.Trust;
        if (!trust.IsFollowedOrListed(request.PackageId))
            return Unknown(correlation, request.PackageId);
        if (trust.TryGetManifest(request.PackageId, out VerifiedManifest? manifest) && manifest.Content.Owner == trust.Self!.User)
            return LocalAnswer.Failure(correlation, Status.Invalid, "A Package the own user owns is always followed.", "packageId");

        await node.UnfollowAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
        return LocalAnswer.Ok<UnfollowPackage, Package>(correlation, await PackageRecords.BuildAsync(node, request.PackageId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary><c>SetPackagePolicy</c> (310): the local retention policy of a known Package (§14.14).</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> SetPackagePolicyAsync(RequestContext context, uint correlation, SetPackagePolicy request, CancellationToken cancellationToken)
    {
        if (!IsPolicy(request.Policy))
            return BadPolicy(correlation, request.Policy);
        MeshNode node = context.Node;
        if (!node.Trust.IsFollowedOrListed(request.PackageId))
            return Unknown(correlation, request.PackageId);

        await node.Writer.SendAsync(new PackageCommand(PackageCommandKind.SetPolicy, request.PackageId, (LocalRetention)(byte)request.Policy), cancellationToken).ConfigureAwait(false);
        return LocalAnswer.Ok<SetPackagePolicy, Package>(correlation, await PackageRecords.BuildAsync(node, request.PackageId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The answer for a Package the Host knows nothing of.</summary>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="package">The Package.</param>
    /// <returns>The answer.</returns>
    public static LocalAnswer Unknown(uint correlation, PackageId package) =>
        LocalAnswer.Failure(correlation, Status.NotFound, $"Package {package} is not known here; a peer lists it once it is shared with this user.", "packageId");

    /// <summary>The answer for a stored object that would put the pinned bytes over the budget (§6.5).</summary>
    /// <param name="correlation">The request's correlation.</param>
    /// <returns>The answer.</returns>
    public static LocalAnswer StoreFull(uint correlation) =>
        LocalAnswer.Failure(correlation, Status.StoreFull, "The store's pinned bytes would exceed its budget; release pins or raise the budget with SetStoreBudget.", string.Empty);

    /// <summary>Whether a list of kinds is strictly ascending, as every such list of LOCAL-SPEC.md is ("ascending" is substance: <c>Invalid</c>).</summary>
    /// <param name="kinds">The kinds as numbers.</param>
    /// <returns><see langword="true"/> when ascending and unique.</returns>
    public static bool Ascending(IEnumerable<ushort> kinds)
    {
        int previous = -1;
        foreach (ushort kind in kinds)
        {
            if (kind <= previous)
                return false;
            previous = kind;
        }

        return true;
    }

    private static bool IsPolicy(LocalPolicy policy) => (byte)policy <= (byte)LocalPolicy.LatestOnly;

    private static LocalAnswer BadPolicy(uint correlation, LocalPolicy policy) =>
        LocalAnswer.Failure(correlation, Status.Invalid, $"Policy {(byte)policy} is not 0, 1 or 2 (LOCAL-SPEC.md §14.14).", "policy");

    private static PackageNonce NewNonce(IEntropy entropy)
    {
        Span<byte> random = stackalloc byte[Hash32.Length];
        entropy.Fill(random);
        return new PackageNonce(new Hash32(random));
    }
}
