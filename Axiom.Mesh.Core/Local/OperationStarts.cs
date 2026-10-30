using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The requests that start an operation (LOCAL-SPEC.md §8.1, §8.6): <c>Fetch</c> and <c>Materialise</c> (§6.4) and
/// <c>PutFile</c> (§9.5). Each checks what it can at once, answers with the operation id, and does the work as the
/// operation: <see cref="ObjectFetcher"/>, <see cref="MaterialiseWork"/>, <see cref="PutFileWork"/>. Busy at 16 running.
/// </summary>
internal static class OperationStarts
{
    /// <summary><c>Fetch</c> (318): the object, and with <c>recursive</c> everything below it; pinned when asked.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> FetchAsync(RequestContext context, uint correlation, Fetch request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        if (!node.Trust.IsFollowed(request.PackageId))
            return ValueTask.FromResult(PackageRequests.Unknown(correlation, request.PackageId));

        (PinReason reason, Hash32 source) = StoreRequests.PinOwner(context.Connection);
        return ValueTask.FromResult(Start(context, correlation, OperationKind.Fetch, ProgressUnit.Objects, LocalAnswer.Ok<Fetch, uint>, async (progress, ct) =>
        {
            var fetcher = new ObjectFetcher(node, request.PackageId, request.Recursive, (hash, size, inner) => request.Pin ? PinAsync(node, hash, size, reason, source, inner) : Task.FromResult(true));
            if (await fetcher.RunAsync(request.Hash, progress, ct).ConfigureAwait(false) is { } failed)
                return failed;
            progress.Report(fetcher.Fetched, fetcher.Fetched, "fetched");
            return OperationOutcome.Done(RecordBytes.Of(new FetchResult { Objects = fetcher.Fetched, Bytes = fetcher.Bytes }));
        }));
    }

    /// <summary>
    /// <c>Materialise</c> (317): a Tree into a directory, or a ChunkList into a file, fetching what is lacking first; the
    /// path must be absolute and <c>existing</c> 1, 2 or 3 (<c>Invalid</c>).
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> MaterialiseAsync(RequestContext context, uint correlation, Materialise request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        if (!Path.IsPathFullyQualified(request.Path))
            return ValueTask.FromResult(LocalAnswer.Failure(correlation, Status.Invalid, "The path must be absolute.", "path"));
        if (request.Existing is not (ExistingPath.Fail or ExistingPath.Replace or ExistingPath.Merge))
            return ValueTask.FromResult(LocalAnswer.Failure(correlation, Status.Invalid, "Existing must be 1 (fail), 2 (replace) or 3 (write into).", "existing"));
        if (!node.Trust.IsFollowed(request.PackageId))
            return ValueTask.FromResult(PackageRequests.Unknown(correlation, request.PackageId));

        var work = new MaterialiseWork(node, request);
        return ValueTask.FromResult(Start(context, correlation, OperationKind.Materialise, ProgressUnit.Bytes, LocalAnswer.Ok<Materialise, uint>, work.RunAsync));
    }

    /// <summary><c>PutFile</c> (515): a regular file cut into Chunks and a ChunkList, each stored and pinned for this service (§9.5).</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> PutFileAsync(RequestContext context, uint correlation, PutFile request, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(request.Path))
            return ValueTask.FromResult(LocalAnswer.Failure(correlation, Status.Invalid, "The path must be absolute.", "path"));

        (PinReason reason, Hash32 source) = StoreRequests.PinOwner(context.Connection);
        var work = new PutFileWork(context.Node, request.Path, reason, source);
        return ValueTask.FromResult(Start(context, correlation, OperationKind.PutFile, ProgressUnit.Bytes, LocalAnswer.Ok<PutFile, uint>, work.RunAsync));
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

    private static LocalAnswer Start(RequestContext context, uint correlation, OperationKind kind, ProgressUnit unit, Func<uint, uint, LocalAnswer> ok, Func<OperationProgress, CancellationToken, Task<OperationOutcome>> work)
    {
        OperationTable operations = context.Connection.Operations;
        if (operations.TryStart(kind, unit, work) is not { } operation)
            return LocalAnswer.Failure(correlation, Status.Busy, $"{Limits.MaxRunningOperations} operations run on this connection already; wait for one to end.", string.Empty);
        return ok(correlation, operation.Id).Then(() => operations.Announce(operation));
    }
}
