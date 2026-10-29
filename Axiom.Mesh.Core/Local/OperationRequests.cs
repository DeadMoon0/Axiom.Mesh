using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary><c>ListOperations</c>, <c>GetOperation</c> and <c>CancelOperation</c> (LOCAL-SPEC.md §8.5), on the connection's own operations.</summary>
internal static class OperationRequests
{
    /// <summary><c>ListOperations</c> (416): this connection's operations, ascending by id.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> ListOperationsAsync(RequestContext context, uint correlation, ListOperations request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(LocalAnswer.Ok<ListOperations, IReadOnlyList<Operation>>(correlation, context.Connection.Operations.List()));

    /// <summary><c>GetOperation</c> (417).</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> GetOperationAsync(RequestContext context, uint correlation, GetOperation request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(context.Connection.Operations.Get(request.OperationId) is { } operation
            ? LocalAnswer.Ok<GetOperation, Operation>(correlation, operation)
            : NotFound(correlation, request.OperationId));

    /// <summary><c>CancelOperation</c> (418): Cancelled at once; an ended one is answered unchanged.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> CancelOperationAsync(RequestContext context, uint correlation, CancelOperation request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(context.Connection.Operations.Cancel(request.OperationId) is { } operation
            ? LocalAnswer.Ok<CancelOperation, Operation>(correlation, operation)
            : NotFound(correlation, request.OperationId));

    private static LocalAnswer NotFound(uint correlation, uint operation) =>
        LocalAnswer.Failure(correlation, Status.NotFound, $"Operation {operation} is not one of this connection's.", "operation");
}
