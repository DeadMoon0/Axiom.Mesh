using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The pairing requests of LOCAL-SPEC.md §6.2: <c>StartPairing</c>, <c>ConfirmPairing</c>, <c>CancelPairing</c>,
/// <c>GetPairing</c>, served by the node's <see cref="PairingShell"/> (PAIR-SPEC.md).
/// </summary>
internal static class PairingRequests
{
    /// <summary><c>StartPairing</c> (272): a pairing in one of the four modes, Waiting; one at a time (<c>Busy</c>).</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> StartPairingAsync(RequestContext context, uint correlation, StartPairing request, CancellationToken cancellationToken)
    {
        (RequestRefusal? refusal, Pairing? pairing) = await context.Node.Pairings.StartAsync(request.Mode, request.Code, cancellationToken).ConfigureAwait(false);
        return refusal?.Answer(correlation) ?? LocalAnswer.Ok<StartPairing, Pairing>(correlation, pairing!);
    }

    /// <summary><c>ConfirmPairing</c> (273): this side's user confirmed that the strings match.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> ConfirmPairingAsync(RequestContext context, uint correlation, ConfirmPairing request, CancellationToken cancellationToken)
    {
        (Status status, Pairing? pairing) = await context.Node.Pairings.ConfirmAsync(request.PairingId, cancellationToken).ConfigureAwait(false);
        return status switch
        {
            Status.Ok => LocalAnswer.Ok<ConfirmPairing, Pairing>(correlation, pairing!),
            Status.Invalid => LocalAnswer.Failure(correlation, status, "The other side has not connected yet: compare the strings once both screens show one.", "pairing"),
            _ => LocalAnswer.Failure(correlation, Status.NotFound, $"Pairing {request.PairingId} is not the current one, or has ended.", "pairing"),
        };
    }

    /// <summary><c>CancelPairing</c> (274): the pairing is Cancelled on both sides.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> CancelPairingAsync(RequestContext context, uint correlation, CancelPairing request, CancellationToken cancellationToken)
    {
        (Status status, Pairing? pairing) = await context.Node.Pairings.CancelAsync(request.PairingId, cancellationToken).ConfigureAwait(false);
        return status == Status.Ok
            ? LocalAnswer.Ok<CancelPairing, Pairing>(correlation, pairing!)
            : LocalAnswer.Failure(correlation, Status.NotFound, $"Pairing {request.PairingId} is not the current one, or has ended.", "pairing");
    }

    /// <summary><c>GetPairing</c> (275): the current or the last pairing; <c>NotFound</c> when there was none.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> GetPairingAsync(RequestContext context, uint correlation, GetPairing request, CancellationToken cancellationToken)
    {
        Pairing? pairing = await context.Node.Pairings.GetAsync(cancellationToken).ConfigureAwait(false);
        return pairing is null
            ? LocalAnswer.Failure(correlation, Status.NotFound, "No pairing ran on this Host since it started.", string.Empty)
            : LocalAnswer.Ok<GetPairing, Pairing>(correlation, pairing);
    }
}
