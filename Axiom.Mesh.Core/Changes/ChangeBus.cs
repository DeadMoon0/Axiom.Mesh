using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>
/// In-process fan-out of <see cref="ChangeSet"/>s from the store writer to whoever reacts: mesh sessions (push,
/// <c>Have</c>), the local port's subscriptions, intake's held buffer, the evictor. Publishing never blocks the writer:
/// each subscriber has a bounded queue, and one that falls behind lapses alone (plan §3.6) and re-reads the state.
/// </summary>
internal sealed class ChangeBus
{
    private readonly object gate = new();
    private ChangeSubscription[] subscribers = [];

    /// <summary>A new subscriber, receiving every change set published from now on.</summary>
    /// <param name="capacity">How many change sets may wait unread before the subscription lapses; at least 1.</param>
    /// <returns>The subscription; dispose it to leave.</returns>
    public ChangeSubscription Subscribe(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        var subscription = new ChangeSubscription(this, capacity);
        lock (gate)
            subscribers = [.. subscribers, subscription];
        return subscription;
    }

    /// <summary>Hands a change set to every subscriber without waiting; a subscriber whose queue is full lapses.</summary>
    /// <param name="changes">The change set.</param>
    public void Publish(ChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ChangeSubscription[] current;
        lock (gate)
            current = subscribers;
        foreach (ChangeSubscription subscription in current)
            subscription.Offer(changes);
    }

    /// <summary>Removes a subscriber.</summary>
    /// <param name="subscription">The subscription.</param>
    internal void Remove(ChangeSubscription subscription)
    {
        lock (gate)
            subscribers = [.. Without(subscribers, subscription)];
    }

    private static IEnumerable<ChangeSubscription> Without(ChangeSubscription[] all, ChangeSubscription gone)
    {
        foreach (ChangeSubscription subscription in all)
        {
            if (!ReferenceEquals(subscription, gone))
                yield return subscription;
        }
    }
}
