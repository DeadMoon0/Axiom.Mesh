using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The telemetry requests of LOCAL-SPEC.md §10.3 (<c>ListSeries</c>, <c>ListKeys</c>, <c>GetTotal</c>,
/// <c>QueryTelemetry</c>) on a Host without telemetry: Host capability bit 2 is clear, so each is <c>NotFound</c>
/// (§14.4; plan decision D7).
/// </summary>
internal static class TelemetryRequests
{
    /// <summary>Answers any telemetry request <c>NotFound</c>.</summary>
    /// <typeparam name="TRequest">The request.</typeparam>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> AbsentAsync<TRequest>(RequestContext context, uint correlation, TRequest request, CancellationToken cancellationToken)
        where TRequest : LocalRequest =>
        ValueTask.FromResult(LocalAnswer.Failure(correlation, Status.NotFound, "This Host keeps no telemetry (Host capability bit 2 is clear).", string.Empty));
}
