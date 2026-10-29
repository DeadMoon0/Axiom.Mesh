using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// An event on the wire (LOCAL-SPEC.md §3.5): the subscription it belongs to and the event. The client's codec reads
/// the body by the event's message id, so <see cref="Event"/> is the typed event.
/// </summary>
public sealed class EventFrame : LocalMessage, IHostMessage
{
    /// <summary>Puts an event in a frame.</summary>
    /// <param name="subscriptionId">The live subscription, never 0.</param>
    /// <param name="event">The event.</param>
    /// <exception cref="ArgumentException"><paramref name="subscriptionId"/> is 0.</exception>
    public EventFrame(uint subscriptionId, LocalEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (subscriptionId == 0)
            throw new ArgumentException("A subscription id is never 0 (LOCAL-SPEC.md §3.2).", nameof(subscriptionId));
        SubscriptionId = subscriptionId;
        Event = @event;
    }

    /// <summary>The subscription.</summary>
    public uint SubscriptionId { get; }

    /// <summary>The event.</summary>
    public LocalEvent Event { get; }

    /// <inheritdoc />
    public override ushort MessageId => Event.MessageId;

    /// <inheritdoc />
    public override string ToString() => $"@{SubscriptionId} {Event}";

    internal override void WritePayload(ref WireWriter writer)
    {
        writer.UInt32BigEndian(SubscriptionId);
        Event.Write(ref writer);
    }
}
