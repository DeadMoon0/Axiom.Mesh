using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The Grant requests of LOCAL-SPEC.md §6.4: <c>ListGrants</c>, <c>SetGrant</c>, <c>RevokeGrant</c>. A Grant is signed by
/// the owner's user key with seq one above the current Grant for the grantee, or 1; a revocation is a Grant with rights 0,
/// which reaches the revoked user although it no longer sees the Package (SPEC.md §9.7).
/// </summary>
internal static class GrantRequests
{
    private const int MaxGrants = 256;

    /// <summary><c>ListGrants</c> (311): the current Grants this Host holds for a Package, revocations included, ascending by grantee.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> ListGrantsAsync(RequestContext context, uint correlation, ListGrants request, CancellationToken cancellationToken)
    {
        if (!context.Node.Trust.TryGetPackage(request.PackageId, out PackageTrust? package) || package.State == PackageState.None)
            return ValueTask.FromResult(PackageRequests.Unknown(correlation, request.PackageId));

        IReadOnlyList<Grant> grants = [.. package.Grants.Values
            .OrderBy(g => g.Content.Grantee)
            .Take(MaxGrants)
            .Select(LocalRecords.GrantOf)];
        return ValueTask.FromResult(LocalAnswer.Ok<ListGrants, IReadOnlyList<Grant>>(correlation, grants));
    }

    /// <summary><c>SetGrant</c> (312): the next Grant for a grantee; rights 0 revokes.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> SetGrantAsync(RequestContext context, uint correlation, SetGrant request, CancellationToken cancellationToken) =>
        GrantAsync(context, correlation, request.PackageId, request.GranteeId, request.Rights, revoking: false, LocalAnswer.Ok<SetGrant, Grant>, cancellationToken);

    /// <summary><c>RevokeGrant</c> (313): <c>SetGrant</c> with rights 0; <c>NotFound</c> for a grantee without a current Grant.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> RevokeGrantAsync(RequestContext context, uint correlation, RevokeGrant request, CancellationToken cancellationToken) =>
        GrantAsync(context, correlation, request.PackageId, request.GranteeId, GrantRights.None, revoking: true, LocalAnswer.Ok<RevokeGrant, Grant>, cancellationToken);

    private static async ValueTask<LocalAnswer> GrantAsync(RequestContext context, uint correlation, PackageId package, UserId grantee, GrantRights rights, bool revoking, Func<uint, Grant, LocalAnswer> ok, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        if (GrantRule.Check(node.Trust, package, grantee, rights, revoking) is { } refusal)
            return refusal.Answer(correlation);

        (Status status, VerifiedGrant? grant) = await node.WithUserKeyAsync<VerifiedGrant>(
            async (signer, ct) =>
            {
                TrustSnapshot trust = node.Trust;
                if (GrantRule.Check(trust, package, grantee, rights, revoking) is { } late)
                    return (late.Status, null);
                trust.TryGetManifest(package, out VerifiedManifest? manifest);
                VerifiedGrant signed = new GrantDraft
                {
                    Grantee = GrantRule.KeyOf(trust, package, grantee)!,
                    Rights = rights,
                    Seq = GrantRule.NextSeq(trust, package, grantee),
                }.Sign(signer, manifest!);
                if (!StoreBudgetRule.Admits(node.Writer.Totals, signed.Bytes.Length))
                    return (Status.StoreFull, null);
                await node.Store.PutAsync(signed, ct).ConfigureAwait(false);
                return (Status.Ok, signed);
            },
            cancellationToken).ConfigureAwait(false);
        return status switch
        {
            Status.Ok => ok(correlation, LocalRecords.GrantOf(grant!)),
            Status.UserKeyAbsent => LocalAnswer.Failure(correlation, status, "Granting needs the user key, which is on another device of this user: grant there.", string.Empty),
            Status.StoreFull => PackageRequests.StoreFull(correlation),
            _ => LocalAnswer.Failure(correlation, status, "The Package or the grantee changed while the Grant was made; ask again.", "packageId"),
        };
    }
}
