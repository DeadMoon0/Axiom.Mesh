using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Axiom.Mesh.Local;

/// <summary>
/// A live subscription (LOCAL-SPEC.md §7): the events of one topic as an <see cref="IAsyncEnumerable{T}"/>, in the order
/// the Host sent them. The enumeration ends when the subscription ends: unsubscribed, or lapsed for being slow, in which
/// case <see cref="Lapse"/> says how many events were dropped and, for topic entries, where to resume.
/// </summary>
/// <typeparam name="TEvent">The topic's event.</typeparam>
/// <remarks>
/// <para>
/// The client queues at most 256 events per subscription, as the Host does (§7.4). When the application does not keep
/// up, the client ends the subscription itself exactly as the Host would: the queued events are still delivered, the
/// rest are counted in <see cref="Lapse"/>, and the Host is told with <c>Unsubscribe</c>. The read loop, the other
/// subscriptions and the requests never wait for a slow reader.
/// </para>
/// <para>
/// When the connection ends the enumeration throws <see cref="LocalConnectionException"/> after the queued events.
/// Disposing unsubscribes.
/// </para>
/// </remarks>
public sealed class Subscription<TEvent> : IAsyncEnumerable<TEvent>, IAsyncDisposable, ISubscriptionSink
    where TEvent : LocalEvent, IEvent<TEvent>
{
    private readonly LocalClient client;
    private readonly Channel<TEvent> queue = Channel.CreateBounded<TEvent>(new BoundedChannelOptions(Limits.MaxQueuedEvents)
    {
        SingleWriter = true,
        SingleReader = false,
        FullMode = BoundedChannelFullMode.Wait,
    });

    private readonly object gate = new();
    private SubscriptionLapsed? lapse;
    private ulong lastSeq;
    private bool ended;

    internal Subscription(LocalClient client, uint id, Filter filter)
    {
        this.client = client;
        Id = id;
        Filter = filter;
    }

    /// <summary>The subscription id this client chose.</summary>
    public uint Id { get; }

    /// <summary>The topic (§7.2).</summary>
    public Topic Topic => TEvent.Topic;

    /// <summary>The filter it was made with.</summary>
    public Filter Filter { get; }

    /// <summary>
    /// Why the subscription ended for being slow, by the Host or by this client: the events dropped and, for topic
    /// entries, the store sequence of the last <c>EntryStored</c> delivered. <see langword="null"/> while it lives or
    /// when it was unsubscribed.
    /// </summary>
    public SubscriptionLapsed? Lapse
    {
        get
        {
            lock (gate)
                return lapse;
        }
    }

    /// <summary>Whether the subscription ended: unsubscribed, lapsed, or its connection ended.</summary>
    public bool IsEnded
    {
        get
        {
            lock (gate)
                return ended;
        }
    }

    /// <summary>Ends the subscription with <c>Unsubscribe</c>; nothing when it ended already.</summary>
    /// <returns>A task that completes when the Host answered.</returns>
    public async ValueTask DisposeAsync()
    {
        if (IsEnded)
            return;

        try
        {
            await client.UnsubscribeAsync(this, CancellationToken.None).ConfigureAwait(false);
        }
        catch (LocalConnectionException)
        {
            // The connection ended; so did the subscription.
        }
    }

    IAsyncEnumerator<TEvent> IAsyncEnumerable<TEvent>.GetAsyncEnumerator(CancellationToken cancellationToken) =>
        queue.Reader.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

    void ISubscriptionSink.Deliver(LocalEvent @event)
    {
        bool lapsedHere = false;
        lock (gate)
        {
            if (ended)
            {
                if (lapse is not null)
                    lapse = lapse with { Dropped = lapse.Dropped + 1 };
                return;
            }

            // An event of another topic on this subscription is a Host defect; it is tolerated like an unknown one (§1.5).
            if (@event is not TEvent typed)
                return;

            if (queue.Writer.TryWrite(typed))
            {
                if (typed is EntryStored stored)
                    lastSeq = stored.Entry.StoreSeq;
                return;
            }

            // The application did not keep up: end the subscription as the Host would (§7.4).
            lapse = new SubscriptionLapsed { Dropped = 1, LastSeq = lastSeq };
            ended = true;
            queue.Writer.TryComplete();
            lapsedHere = true;
        }

        if (lapsedHere)
            client.ReleaseLapsed(this);
    }

    void ISubscriptionSink.Lapse(SubscriptionLapsed lapsed)
    {
        lock (gate)
        {
            if (lapse is not null)
            {
                lapse = lapse with { Dropped = lapse.Dropped + lapsed.Dropped };
                return;
            }

            lapse = lapsed;
            ended = true;
            queue.Writer.TryComplete();
        }
    }

    void ISubscriptionSink.End(Exception? error)
    {
        lock (gate)
        {
            ended = true;
            queue.Writer.TryComplete(lapse is null ? error : null);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Subscription {Id} to {Topic}{(IsEnded ? " (ended)" : "")}";
}
