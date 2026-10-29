using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// Decides whether a stored object may be sent to a peer's user in answer to <c>Want</c> (SPEC.md §9.7, §9.8, §11.3): a
/// current Roster under §9.8, a current Manifest or Grant under §9.7, an Entry when its Package is visible, and a Chunk,
/// ChunkList, Tree or opaque object when any visible Package's Entry references it, through Trees and ChunkLists; else
/// <c>NotFound</c>, which on the wire is the same as an object never held. One per session; it remembers the objects it
/// found visible under the trust it found them with.
/// </summary>
internal sealed class ServedObjects
{
    private const int MaxVisited = 4_096;
    private const int MaxRemembered = 65_536;

    private readonly UserId peer;
    private HashSet<ObjectHash> visible = [];
    private long trustVersion = -1;

    /// <summary>The decisions for one peer.</summary>
    /// <param name="peer">The peer's user.</param>
    public ServedObjects(UserId peer) => this.peer = peer;

    /// <summary>Resolves one wanted hash.</summary>
    /// <param name="context">A read unit of work on the index.</param>
    /// <param name="hash">The hash wanted.</param>
    /// <param name="trust">The node's trust now.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The object's size when it is stored and the peer may see it; <see langword="null"/> for <c>NotFound</c>.</returns>
    public async Task<int?> ResolveAsync(LdbContext context, ObjectHash hash, TrustSnapshot trust, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(trust);
        if (trust.Version != trustVersion)
        {
            visible = [];
            trustVersion = trust.Version;
        }

        ObjectRow? row = await context.Set<ObjectRow>().FindAsync(RowKeys.Of(hash.Value), cancellationToken).ConfigureAwait(false);
        if (row is null || row.EvictPending)
            return null;

        bool may = (ObjectTag)row.Tag switch
        {
            ObjectTag.Roster or ObjectTag.Manifest or ObjectTag.Grant => MayServeSigned(hash, trust),
            ObjectTag.Entry => await MayServeEntryAsync(context, hash, trust, cancellationToken).ConfigureAwait(false),
            _ => visible.Contains(hash) || await MayServeThroughAsync(context, hash, trust, cancellationToken).ConfigureAwait(false),
        };
        return may ? (int)row.Size : null;
    }

    // Only the current Roster of a known user, Manifest or Grant of a followed Package is served (SPEC.md §8.8, §9.7, §9.8).
    private bool MayServeSigned(ObjectHash hash, TrustSnapshot trust)
    {
        if (!trust.TryGetCurrent(hash, out CurrentSigned held))
            return false;
        return held.Tag switch
        {
            ObjectTag.Roster => trust.MayServeRoster(held.User, peer),
            ObjectTag.Manifest => trust.MayList(peer, held.Package),
            ObjectTag.Grant => trust.TryGetPackage(held.Package, out PackageTrust? package) && package.Manifest is { } manifest
                && Visibility.MayClaimGrant(peer, manifest.Content.Owner, held.User),
            _ => false,
        };
    }

    private async Task<bool> MayServeEntryAsync(LdbContext context, ObjectHash hash, TrustSnapshot trust, CancellationToken cancellationToken)
    {
        EntryRow? entry = await context.Set<EntryRow>().FindAsync(RowKeys.Of(hash.Value), cancellationToken).ConfigureAwait(false);
        return entry is not null && trust.MayList(peer, new PackageId(RowKeys.Hash(entry.Package)));
    }

    // Upwards through the references (SPEC.md §9.7): breadth first to the Entries that name the object, bounded.
    private async Task<bool> MayServeThroughAsync(LdbContext context, ObjectHash hash, TrustSnapshot trust, CancellationToken cancellationToken)
    {
        var seen = new HashSet<ObjectHash> { hash };
        var frontier = new Queue<ObjectHash>();
        frontier.Enqueue(hash);
        while (frontier.TryDequeue(out ObjectHash next) && seen.Count <= MaxVisited)
        {
            List<ReferenceRow> referrers = await context.Set<ReferenceRow>().ReadOnlyQuery()
                .Where(ReferenceRow.Columns.Target == RowKeys.Of(next.Value))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (ReferenceRow reference in referrers)
            {
                var referrer = new ObjectHash(RowKeys.Hash(reference.Referrer));
                if (!seen.Add(referrer))
                    continue;
                if (visible.Contains(referrer)
                    || await MayServeEntryAsync(context, referrer, trust, cancellationToken).ConfigureAwait(false))
                {
                    Remember(referrer);
                    Remember(hash);
                    return true;
                }

                frontier.Enqueue(referrer);
            }
        }

        return false;
    }

    private void Remember(ObjectHash hash)
    {
        if (visible.Count >= MaxRemembered)
            visible.Clear();
        visible.Add(hash);
    }
}
