using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The requests that start an operation (LOCAL-SPEC.md §8.1, §8.6): <c>Fetch</c> (§6.4), the one kind left since draft 4
/// moved <c>Materialise</c> and <c>PutFile</c> out of the Host into the client's helpers. It checks what it can at once,
/// answers with the operation id, and does the work as the operation (<see cref="ObjectFetcher"/>). Busy at 16 running.
/// </summary>
internal static class OperationStarts
{
    /// <summary><c>Fetch</c> (318): the object, and with <c>recursive</c> everything below it; pinned when asked.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> FetchAsync(RequestContext context, uint correlation, Fetch request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        if (!node.Trust.IsFollowed(request.PackageId))
            return PackageRequests.Unknown(correlation, request.PackageId);
        if (!await IsOfPackageAsync(node, request.PackageId, request.Hash, cancellationToken).ConfigureAwait(false))
            return LocalAnswer.Failure(correlation, Status.NotFound, $"No stored Entry of Package {request.PackageId} references {request.Hash}; fetch what the Package's history names (LOCAL-SPEC.md §6.4).", "hash");

        (PinReason reason, Hash32 source) = StoreRequests.PinOwner(context.Connection);
        return Start(context, correlation, OperationKind.Fetch, ProgressUnit.Objects, LocalAnswer.Ok<Fetch, uint>, async (progress, ct) =>
        {
            var fetcher = new ObjectFetcher(node, request.PackageId, request.Recursive, (hash, size, inner) => request.Pin ? PinAsync(node, hash, size, reason, source, inner) : Task.FromResult(true));
            if (await fetcher.RunAsync(request.Hash, progress, ct).ConfigureAwait(false) is { } failed)
                return failed;
            progress.Report(fetcher.Fetched, fetcher.Fetched, "fetched");
            return OperationOutcome.Done(RecordBytes.Of(new FetchResult { Objects = fetcher.Fetched, Bytes = fetcher.Bytes }));
        });
    }

    /// <summary>Pins a stored object for a role, unless that would put the pinned bytes over the budget.</summary>
    /// <param name="node">The node.</param>
    /// <param name="hash">The object.</param>
    /// <param name="size">Its size.</param>
    /// <param name="reason">User or Service.</param>
    /// <param name="source">The pin's source.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns><see langword="false"/> for <c>StoreFull</c>.</returns>
    public static async Task<bool> PinAsync(MeshNode node, ObjectHash hash, long size, PinReason reason, Hash32 source, CancellationToken cancellationToken)
    {
        if (!StoreBudgetRule.Admits(node.Writer.Totals, size))
            return false;
        await node.Writer.SendAsync(new PinCommand(PinCommandKind.Pin, [hash], reason, source), cancellationToken).ConfigureAwait(false);
        return true;
    }

    // LOCAL-SPEC.md §6.4: the object is one of the Package's, a stored Entry of it or reached from one through the reference
    // rows (SPEC.md §10.3), before anything is wanted for it (STRUCTURAL-DEBT.md 42).
    private static async Task<bool> IsOfPackageAsync(MeshNode node, PackageId package, ObjectHash hash, CancellationToken cancellationToken)
    {
        await using LdbContext context = await LdbContext.OpenAsync(node.Writer.Database, cancellationToken).ConfigureAwait(false);
        byte[] packageKey = RowKeys.Of(package.Value);
        async Task<bool> IsEntryOfThePackage(ObjectHash candidate) =>
            await context.Set<EntryRow>().FindAsync(RowKeys.Of(candidate.Value), cancellationToken).ConfigureAwait(false) is { } row
            && row.Package.AsSpan().SequenceEqual(packageKey);
        return await IsEntryOfThePackage(hash).ConfigureAwait(false)
            || await ReferenceWalk.UpwardAsync(context, hash, IsEntryOfThePackage, cancellationToken).ConfigureAwait(false) is not null;
    }

    private static LocalAnswer Start(RequestContext context, uint correlation, OperationKind kind, ProgressUnit unit, Func<uint, uint, LocalAnswer> ok, Func<OperationProgress, CancellationToken, Task<OperationOutcome>> work)
    {
        OperationTable operations = context.Connection.Operations;
        if (operations.TryStart(kind, unit, work) is not { } operation)
            return LocalAnswer.Failure(correlation, Status.Busy, $"{Limits.MaxRunningOperations} operations run on this connection already; wait for one to end.", string.Empty);
        return ok(correlation, operation.Id).Then(() => operations.Announce(operation));
    }
}
