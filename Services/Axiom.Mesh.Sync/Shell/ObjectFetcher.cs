using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>
/// Brings a Tree with everything below it into the store (LOCAL-SPEC.md §6.4 <c>Fetch</c>, recursive), following the operation
/// on topic <c>operations</c> subscribed before the request, so its end is never missed.
/// </summary>
internal static class ObjectFetcher
{
    /// <summary>Fetches a Tree and everything below it.</summary>
    /// <param name="client">The service's connection.</param>
    /// <param name="package">The Archive whose Version names the Tree.</param>
    /// <param name="tree">The Tree.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns><see langword="true"/> when everything is stored; <see langword="false"/> when no peer supplied it (yet).</returns>
    public static async Task<bool> FetchAsync(LocalClient client, PackageId package, ObjectHash tree, CancellationToken cancellationToken)
    {
        LocalResult<Subscription<OperationChanged>> subscribed = await client.SubscribeAsync<OperationChanged>(Filter.Any, cancellationToken).ConfigureAwait(false);
        if (!subscribed.IsOk)
            return false;
        await using Subscription<OperationChanged> operations = subscribed.Value;
        LocalResult<uint> started = await client.SendAsync(new Fetch { PackageId = package, Hash = tree, Recursive = true }, cancellationToken).ConfigureAwait(false);
        if (!started.IsOk)
            return false;
        await foreach (OperationChanged changed in operations.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (changed.Operation.OperationId != started.Value || changed.Operation.State == OperationState.Running)
                continue;
            return changed.Operation.State == OperationState.Done;
        }

        return false;
    }
}
