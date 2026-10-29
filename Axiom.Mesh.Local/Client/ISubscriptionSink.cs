using System;

namespace Axiom.Mesh.Local;

/// <summary>Where the client's read loop hands the events of one subscription.</summary>
internal interface ISubscriptionSink
{
    /// <summary>The subscription id.</summary>
    uint Id { get; }

    /// <summary>One event of the subscription, in order; never blocks the read loop.</summary>
    void Deliver(LocalEvent @event);

    /// <summary>The Host ended the subscription for being slow (§7.4).</summary>
    void Lapse(SubscriptionLapsed lapsed);

    /// <summary>The subscription ended: unsubscribed (no error) or the connection ended (the error).</summary>
    void End(Exception? error);
}
