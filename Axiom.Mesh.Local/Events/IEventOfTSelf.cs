namespace Axiom.Mesh.Local;

/// <summary>What an event states about itself in LOCAL-SPEC.md: its message id (§14.1) and its topic (§7.2, §14.5).</summary>
/// <typeparam name="TSelf">The event itself.</typeparam>
public interface IEvent<TSelf> : IWireRecord<TSelf>
    where TSelf : LocalEvent, IEvent<TSelf>
{
    /// <summary>The message id, in the event range 1024 to 1535 (§3.3, §14.1).</summary>
    static abstract ushort Id { get; }

    /// <summary>
    /// The topic whose subscriptions receive it; <see cref="Topic.Unassigned"/> for <c>SubscriptionLapsed</c>, which
    /// ends a subscription of any topic.
    /// </summary>
    static abstract Topic Topic { get; }
}
