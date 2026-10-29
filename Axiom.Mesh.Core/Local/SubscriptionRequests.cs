using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// <c>Subscribe</c> and <c>Unsubscribe</c> (LOCAL-SPEC.md §7.1): the response to a <c>Subscribe</c> is written before the
/// subscription's first event, and no event of a subscription follows the response to its <c>Unsubscribe</c>.
/// </summary>
internal static class SubscriptionRequests
{
    /// <summary><c>Subscribe</c> (400).</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> SubscribeAsync(RequestContext context, uint correlation, Subscribe request, CancellationToken cancellationToken)
    {
        if (FilterRule.Check(request.Topic, request.Filter) is { } wrong)
            return LocalAnswer.Failure(correlation, Status.Invalid, "The topic or the filter is not one this protocol defines.", wrong);

        // Host capability bit 2 is clear: no telemetry is kept, and a telemetry subscription is NotFound (§14.4, D7).
        if (request.Topic == Topic.Telemetry)
            return LocalAnswer.Failure(correlation, Status.NotFound, "This Host keeps no telemetry.", "topic");

        StoreWriter writer = context.Node.Writer;
        ulong liveAfter = writer.Totals.StoreSeq;
        IReadOnlyList<ulong> replay = [];
        if (request.Topic == Topic.Entries && request.Filter.FromSeq != 0)
        {
            List<ulong> plan = await EntryRecords.ReplayPlanAsync(writer.Database, request.Filter, request.Filter.FromSeq, liveAfter, cancellationToken).ConfigureAwait(false);
            if (plan.Count > Limits.MaxReplayEntries)
            {
                return LocalAnswer.Failure(correlation, Status.GapTooLarge, $"More than {Limits.MaxReplayEntries} stored Entries match from store sequence {request.Filter.FromSeq}: page with ListStored first, then subscribe from where it ends.", "filter.fromSeq");
            }

            replay = plan;
        }

        var subscription = new LocalSubscription(request.SubscriptionId, request.Topic, request.Filter, liveAfter, replay);
        Outbox outbox = context.Connection.Outbox;
        return outbox.Add(subscription) switch
        {
            Status.AlreadyExists => LocalAnswer.Failure(correlation, Status.AlreadyExists, $"Subscription {request.SubscriptionId} is live on this connection already.", "subscription"),
            Status.Busy => LocalAnswer.Failure(correlation, Status.Busy, $"This connection holds {Limits.MaxSubscriptions} subscriptions, the most it may.", "subscription"),
            _ => LocalAnswer.Ok<Subscribe, NoContent>(correlation, default).Then(() => outbox.Answered(subscription)),
        };
    }

    /// <summary><c>Unsubscribe</c> (401).</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> UnsubscribeAsync(RequestContext context, uint correlation, Unsubscribe request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(context.Connection.Outbox.Remove(request.SubscriptionId)
            ? LocalAnswer.Ok<Unsubscribe, NoContent>(correlation, default)
            : LocalAnswer.Failure(correlation, Status.NotFound, $"Subscription {request.SubscriptionId} is not live on this connection.", "subscription"));
}
