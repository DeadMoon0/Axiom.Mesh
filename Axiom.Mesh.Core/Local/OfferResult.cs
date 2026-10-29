namespace Axiom.Mesh.Core;

/// <summary>What happened to an event offered to a subscription (LOCAL-SPEC.md §7.4).</summary>
internal enum OfferResult
{
    /// <summary>Not taken: the subscription lapsed or ended, or the event is the replay's or older than the subscription.</summary>
    Ignored = 0,

    /// <summary>Queued.</summary>
    Queued = 1,

    /// <summary>It broke the subscription's bound: the queue was discarded and a <c>SubscriptionLapsed</c> queued instead.</summary>
    Lapsed = 2,

    /// <summary>The connection holds more than 64 MiB: it is to be closed with <c>Bye(Unresponsive)</c>.</summary>
    Overflow = 3,
}
