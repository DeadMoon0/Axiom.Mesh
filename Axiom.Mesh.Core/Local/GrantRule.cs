using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The rules of LOCAL-SPEC.md §6.4 for <c>SetGrant</c> and <c>RevokeGrant</c>, as functions of trust: the Package is followed
/// with its Manifest (<c>NotFound</c>), the own user owns it (<c>NotOwner</c>), the grantee is a known user (<c>NotFound</c>)
/// other than the owner (<c>Invalid</c>), the rights are 0, 1 or 2 (<c>Invalid</c>), and a revocation needs a current Grant
/// with rights (<c>NotFound</c>). The next Grant's seq is one above the current one, or 1 (SPEC.md §8.9).
/// </summary>
internal static class GrantRule
{
    /// <summary>Decides a <c>SetGrant</c> or <c>RevokeGrant</c>.</summary>
    /// <param name="trust">The node's trust, with an identity.</param>
    /// <param name="package">The Package.</param>
    /// <param name="grantee">The grantee.</param>
    /// <param name="rights">The rights to grant; <see cref="GrantRights.None"/> revokes.</param>
    /// <param name="revoking">The request is <c>RevokeGrant</c>.</param>
    /// <returns><see langword="null"/> when the Grant may be made; else why not.</returns>
    public static RequestRefusal? Check(TrustSnapshot trust, PackageId package, UserId grantee, GrantRights rights, bool revoking)
    {
        if (!trust.TryGetPackage(package, out PackageTrust? held) || !held.IsFollowed || held.Manifest is not { } manifest)
            return new RequestRefusal(Status.NotFound, $"Package {package} is not followed here with its Manifest.", "packageId");
        if ((byte)rights > (byte)GrantRights.Write)
            return new RequestRefusal(Status.Invalid, $"Rights {(byte)rights} are not 0, 1 or 2 (SPEC.md §8.9).", "rights");
        if (manifest.Content.Owner != trust.Self!.User)
            return new RequestRefusal(Status.NotOwner, $"Package {package} is not the own user's; only its owner grants it.", "packageId");
        if (grantee == manifest.Content.Owner)
            return new RequestRefusal(Status.Invalid, "The owner holds every right already and is never a grantee.", "granteeId");
        if (KeyOf(trust, package, grantee) is null)
            return new RequestRefusal(Status.NotFound, $"User {grantee} is not known here; pair with the user first.", "granteeId");
        if (revoking && (!held.Grants.TryGetValue(grantee, out VerifiedGrant? current) || current.Content.Rights == GrantRights.None))
            return new RequestRefusal(Status.NotFound, $"User {grantee} holds no current Grant of Package {package}.", "granteeId");
        return null;
    }

    /// <summary>The grantee's user key: from its Roster's user, or from a Grant that names it.</summary>
    /// <param name="trust">The node's trust.</param>
    /// <param name="package">The Package.</param>
    /// <param name="grantee">The grantee.</param>
    /// <returns>The key; <see langword="null"/> for a user that is not known.</returns>
    public static PublicKey? KeyOf(TrustSnapshot trust, PackageId package, UserId grantee)
    {
        if (!trust.Known.Contains(grantee))
            return null;
        if (trust.TryGetUser(grantee, out UserTrust? user))
            return user.Key;
        return trust.TryGetGrant(package, grantee, out VerifiedGrant? grant) ? grant.Content.GranteeKey : null;
    }

    /// <summary>The seq of the next Grant for a grantee: one above the current one, or 1.</summary>
    /// <param name="trust">The node's trust.</param>
    /// <param name="package">The Package.</param>
    /// <param name="grantee">The grantee.</param>
    /// <returns>The seq.</returns>
    public static ulong NextSeq(TrustSnapshot trust, PackageId package, UserId grantee) =>
        trust.TryGetGrant(package, grantee, out VerifiedGrant? current) ? current.Content.Seq + 1 : 1;
}
