using System;
using System.Collections.Generic;
using WireState = Axiom.Mesh.Protocol.PackageState;

namespace Axiom.Mesh.Core;

/// <summary>
/// What to want, as pure functions of what this node holds and what a peer lists or what a stored object names:
/// <see cref="Plan"/> is SPEC.md §10.2 steps 1 to 3 for one <c>PackageState</c>, <see cref="Walk"/> is the history walk
/// of §10.3 for one stored object under the retention policy of §11.2. Neither looks at the store or the clock; the
/// caller drops what is already held or wanted.
/// </summary>
internal static class ReplicationPlanner
{
    /// <summary>
    /// SPEC.md §10.2 for one Package a peer lists. A Package the node does not know yields nothing (it is listed first);
    /// one whose Manifest the node does not hold yields the listed Manifest and nothing else, followed or listed (§10.6);
    /// a listed Package yields nothing more. For a followed Package: the Manifest when the peer's is newer (higher seq, or
    /// equal with a lower hash), every Grant the node does not hold, and every Head the node lacks whose device is, in the
    /// rosters it holds, a not-removed device of the owner or of a current <c>Write</c> grantee, with a clock above the
    /// node's Head for that device.
    /// </summary>
    /// <param name="mine">What this node holds of the Package.</param>
    /// <param name="theirs">What the peer lists.</param>
    /// <param name="trust">The rosters and Grants that say who may write.</param>
    /// <returns>The hashes to want, in the order of the steps.</returns>
    public static IReadOnlyList<PlannedWant> Plan(PackageView mine, WireState theirs, ITrustSnapshotView trust)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);
        ArgumentNullException.ThrowIfNull(trust);
        if (mine.Package != theirs.Package)
            throw new ArgumentException($"The peer's state is of Package {theirs.Package}, the node's view of {mine.Package}.", nameof(theirs));
        if (mine.State == PackageState.None || theirs.ManifestHash.Value.IsZero)
            return [];

        // Step 1: without a Manifest, nothing of the Package can be verified; want it first and nothing else (§10.2).
        if (mine.Manifest is not { } held)
            return [new PlannedWant(theirs.ManifestHash, WantKind.Manifest)];
        if (mine.State != PackageState.Followed)
            return [];

        var wants = new List<PlannedWant>();
        if (CurrentObject.Supersedes(new ObjectSeq(theirs.ManifestSeq, theirs.ManifestHash), held))
            wants.Add(new PlannedWant(theirs.ManifestHash, WantKind.Manifest));

        // Step 2: every Grant the node does not hold.
        foreach (ObjectHash grant in theirs.Grants)
        {
            if (!mine.Grants.Contains(grant))
                wants.Add(new PlannedWant(grant, WantKind.Grant));
        }

        // Step 3: Heads, both lists ascending by device id, walked side by side.
        IReadOnlyList<Head> own = mine.Heads;
        int at = 0;
        foreach (Head head in theirs.Heads)
        {
            while (at < own.Count && own[at].Device < head.Device)
                at++;
            bool newer = at >= own.Count || own[at].Device != head.Device || head.Clock > own[at].Clock;
            if (newer && MayWrite(head.Device, mine.Owner, mine.Package, trust))
                wants.Add(new PlannedWant(head.Entry, WantKind.Head));
        }

        return wants;
    }

    /// <summary>
    /// SPEC.md §10.3 for one stored object of a followed Package: an Entry's parents always (until held Entries are
    /// reached, which the caller checks), then the objects the retention row keeps: what an Entry's envelope references,
    /// a Tree's entries, a ChunkList's Chunks (§11.2). Under the latest-only row an Archive keeps only what its newest
    /// Version references.
    /// </summary>
    /// <param name="tag">The stored object's tag.</param>
    /// <param name="links">What it names.</param>
    /// <param name="row">The retention row that applies to the Package.</param>
    /// <param name="kind">The Package's kind.</param>
    /// <returns>The hashes to want, parents first.</returns>
    public static IReadOnlyList<PlannedWant> Walk(ObjectTag tag, ObjectLinks links, RetentionRow row, PackageKind kind)
    {
        ArgumentNullException.ThrowIfNull(links);
        if (links.Parents.Count == 0 && links.Children.Count == 0)
            return [];

        var wants = new List<PlannedWant>(links.Parents.Count + links.Children.Count);
        foreach (ObjectHash parent in links.Parents)
            wants.Add(new PlannedWant(parent, WantKind.Parent));

        bool latestOnlyArchive = tag == ObjectTag.Entry && kind == PackageKind.Archive && row == RetentionRow.LatestOnly;
        if (latestOnlyArchive && !links.IsNewestVersion)
            return wants;

        foreach (ObjectLink child in links.Children)
        {
            // An envelope reference names a Tree, a ChunkList or anything else; the row keeps them or not as it keeps a Tree.
            ObjectTag target = child.Tag == ObjectTag.Unassigned ? ObjectTag.Tree : child.Tag;
            if (RetentionRule.Keeps(row, kind, tag, target))
                wants.Add(new PlannedWant(child.Hash, WantKind.Payload));
        }

        return wants;
    }

    // SPEC.md §10.2 step 3: a device of the owner, or of a current Write grantee, not removed, in the rosters held.
    private static bool MayWrite(DeviceId device, UserId owner, PackageId package, ITrustSnapshotView trust)
    {
        if (!trust.TryGetDevice(device, out TrustedDevice writer) || writer.IsRemoved)
            return false;
        return writer.User == owner
            || (trust.TryGetGrant(package, writer.User, out VerifiedGrant? grant) && grant.Content.Rights == GrantRights.Write);
    }
}
