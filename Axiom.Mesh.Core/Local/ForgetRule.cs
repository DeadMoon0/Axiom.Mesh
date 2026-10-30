using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// Whether a user may be forgotten (LOCAL-SPEC.md §6.2 <c>ForgetUser</c>), as a function of trust: not the own user, not a
/// user who holds a current Grant from the own user (the app revokes first), not the owner or a current grantee of a
/// followed Package the own user does not own (SPEC.md §1.2 keeps such a user known), and only a user that is known.
/// </summary>
internal static class ForgetRule
{
    /// <summary>Decides a <c>ForgetUser</c>.</summary>
    /// <param name="trust">The node's trust, with an identity.</param>
    /// <param name="user">The user to forget.</param>
    /// <returns><see langword="null"/> when the user may be forgotten; else why not.</returns>
    public static RequestRefusal? Check(TrustSnapshot trust, UserId user)
    {
        UserId self = trust.Self!.User;
        if (user == self)
            return new RequestRefusal(Status.Invalid, "The own user cannot be forgotten.", "userId");
        if (!trust.Known.Contains(user))
            return new RequestRefusal(Status.NotFound, $"User {user} is not known here.", "userId");

        foreach (PackageTrust package in trust.Packages.Where(p => p.IsFollowed && p.Manifest is not null))
        {
            UserId owner = package.Manifest!.Content.Owner;
            bool granted = package.Grants.TryGetValue(user, out VerifiedGrant? grant) && grant.Content.Rights != GrantRights.None;
            if (owner == self && granted)
                return new RequestRefusal(Status.Invalid, $"User {user} holds a Grant of Package {package.Id}; revoke it first.", "userId");
            if (owner != self && (owner == user || granted))
                return new RequestRefusal(Status.Invalid, $"User {user} owns or is granted the followed Package {package.Id}; unfollow it first.", "userId");
        }

        return null;
    }
}
