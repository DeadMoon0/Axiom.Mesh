using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// The upward walk over the reference rows (SPEC.md §9.7, §10.3): from an object to the objects that name it, an Entry's
/// envelope naming a Tree, ChunkList or anything else, a Tree naming its entries, a ChunkList its Chunks, breadth first
/// and bounded, until an object the caller accepts. Serving a content object to a peer and fetching one for a Package
/// both ask it: which stored Entry, if any, reaches this object.
/// </summary>
internal static class ReferenceWalk
{
    /// <summary>The most objects one walk visits; a walk that reaches none accepted within them finds nothing.</summary>
    public const int MaxVisited = 4_096;

    /// <summary>Walks upwards from <paramref name="hash"/> to the first referrer <paramref name="accepts"/> accepts.</summary>
    /// <param name="context">A read unit of work on the index.</param>
    /// <param name="hash">The object to start from; itself never asked.</param>
    /// <param name="accepts">Whether a referrer ends the walk.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The path from the accepted referrer down to <paramref name="hash"/>, both included; <see langword="null"/> when none was reached.</returns>
    public static async Task<IReadOnlyList<ObjectHash>?> UpwardAsync(LdbContext context, ObjectHash hash, Func<ObjectHash, Task<bool>> accepts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(accepts);
        var reachedFrom = new Dictionary<ObjectHash, ObjectHash> { [hash] = hash };
        var frontier = new Queue<ObjectHash>();
        frontier.Enqueue(hash);
        while (frontier.TryDequeue(out ObjectHash next) && reachedFrom.Count <= MaxVisited)
        {
            List<ReferenceRow> referrers = await context.Set<ReferenceRow>().ReadOnlyQuery()
                .Where(ReferenceRow.Columns.Target == RowKeys.Of(next.Value))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (ReferenceRow reference in referrers)
            {
                var referrer = new ObjectHash(RowKeys.Hash(reference.Referrer));
                if (!reachedFrom.TryAdd(referrer, next))
                    continue;
                if (await accepts(referrer).ConfigureAwait(false))
                    return PathOf(referrer, hash, reachedFrom);
                frontier.Enqueue(referrer);
            }
        }

        return null;
    }

    private static List<ObjectHash> PathOf(ObjectHash top, ObjectHash start, Dictionary<ObjectHash, ObjectHash> reachedFrom)
    {
        var path = new List<ObjectHash>();
        for (ObjectHash on = top; ; on = reachedFrom[on])
        {
            path.Add(on);
            if (on == start)
                return path;
        }
    }
}
