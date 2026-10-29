using System;
using System.Collections.Generic;

namespace Axiom.Mesh;

/// <summary>
/// What a node may show a peer (SPEC.md §9.7, §9.8), decided for the peer's user. Every serving node enforces it
/// independently, for listings, <c>Have</c>s, answers to <c>Want</c> and Roster queries alike.
/// </summary>
public static class Visibility
{
    /// <summary>
    /// Whether a Package, and every object of it, may be listed, claimed or sent to a peer: its user is the owner or holds
    /// a current <c>Read</c> or <c>Write</c> Grant (SPEC.md §7.2, §9.7).
    /// </summary>
    /// <param name="peerUser">The peer's user.</param>
    /// <param name="package">The Package's audience.</param>
    /// <returns><see langword="true"/> when the peer may see the Package.</returns>
    public static bool MayList(UserId peerUser, PackageAudience package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return package.Includes(peerUser);
    }

    /// <summary>
    /// Whether a Grant may be listed, claimed or sent to a peer: to the owner's devices every current Grant, to anyone
    /// else only the Grant naming their own user, whatever its rights, so that a revocation reaches the revoked user
    /// (SPEC.md §9.7).
    /// </summary>
    /// <param name="peerUser">The peer's user.</param>
    /// <param name="owner">The Package's owner.</param>
    /// <param name="grantee">The Grant's grantee.</param>
    /// <returns><see langword="true"/> when the peer may see the Grant.</returns>
    public static bool MayClaimGrant(UserId peerUser, UserId owner, UserId grantee) => peerUser == owner || peerUser == grantee;

    /// <summary>
    /// Whether an object held under several Packages (a Chunk, ChunkList or Tree that Entries reference) may be sent to
    /// a peer: when any Package whose Entries reference it is visible to them; otherwise the answer is <c>NotFound</c>
    /// (SPEC.md §9.7).
    /// </summary>
    /// <param name="peerUser">The peer's user.</param>
    /// <param name="referencing">The audiences of the Packages whose Entries reference the object.</param>
    /// <returns><see langword="true"/> when the peer may see the object.</returns>
    public static bool MayServeThrough(UserId peerUser, IEnumerable<PackageAudience> referencing)
    {
        ArgumentNullException.ThrowIfNull(referencing);
        foreach (PackageAudience package in referencing)
        {
            if (MayList(peerUser, package))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a Roster may be served to a peer, after <c>Hello</c> or in answer to <c>RosterQuery</c>: the peer's own
    /// user's, this node's own user's, and those of the owner and current grantees of Packages the peer may see
    /// (SPEC.md §9.8). Any other query is answered <c>RosterNotFound</c>.
    /// </summary>
    /// <param name="rosterUser">The user whose Roster is asked for.</param>
    /// <param name="peerUser">The peer's user.</param>
    /// <param name="ownUser">This node's own user.</param>
    /// <param name="packages">The audiences of the Packages this node holds.</param>
    /// <returns><see langword="true"/> when the Roster may be served.</returns>
    public static bool MayServeRoster(UserId rosterUser, UserId peerUser, UserId ownUser, IEnumerable<PackageAudience> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        if (rosterUser == peerUser || rosterUser == ownUser)
            return true;

        foreach (PackageAudience package in packages)
        {
            if (MayList(peerUser, package) && package.Includes(rosterUser))
                return true;
        }

        return false;
    }
}
