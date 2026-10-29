using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// What one connection has to write, and the bounds on it (LOCAL-SPEC.md §7.4, §12): a control lane of responses and
/// session frames, written first, and one queue per subscription, written round robin once its <c>Subscribe</c> was
/// answered. A subscription's queue holds at most 256 events and 16 MiB; the connection holds at most 64 MiB of encoded
/// frames that the pipe has not accepted, responses included. The connection's one writer takes from here; producers
/// (requests, the event hub, operations, liveness) never wait.
/// </summary>
internal sealed class Outbox
{
    private readonly object gate = new();
    private readonly Queue<OutFrame> control = new();
    private readonly Dictionary<uint, LocalSubscription> subscriptions = [];
    private readonly List<LocalSubscription> order = [];
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly Action<long> account;
    private long held;
    private int cursor;
    private bool closed;
    private int drained;

    /// <summary>An empty outbox.</summary>
    /// <param name="account">Told every change of the bytes held, for the Host's bound of 256 MiB across connections.</param>
    public Outbox(Action<long> account) => this.account = account;

    /// <summary>Encoded bytes held for the connection: queued frames and the frame being written.</summary>
    public long HeldBytes => Interlocked.Read(ref held);

    /// <summary>Live and lapsing subscriptions.</summary>
    public int Subscriptions
    {
        get
        {
            lock (gate)
                return subscriptions.Count;
        }
    }

    /// <summary>Queues a control frame.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns><see langword="false"/> when the connection now holds more than 64 MiB, or is closed.</returns>
    public bool Enqueue(OutFrame frame)
    {
        lock (gate)
        {
            if (closed)
                return false;
            control.Enqueue(frame);
        }

        bool fits = Hold(frame.Size);
        Wake();
        return fits;
    }

    /// <summary>Adds a subscription (§7.1).</summary>
    /// <param name="subscription">The subscription, not yet answered.</param>
    /// <returns><see cref="Status.Ok"/>, <see cref="Status.AlreadyExists"/> or <see cref="Status.Busy"/>.</returns>
    public Status Add(LocalSubscription subscription)
    {
        lock (gate)
        {
            if (subscriptions.ContainsKey(subscription.Id))
                return Status.AlreadyExists;
            if (subscriptions.Count >= Limits.MaxSubscriptions)
                return Status.Busy;
            subscriptions.Add(subscription.Id, subscription);
            order.Add(subscription);
            return Status.Ok;
        }
    }

    /// <summary>Ends a live subscription and discards what it queued (§7.1): no event of it is written after this.</summary>
    /// <param name="id">The subscription id.</param>
    /// <returns><see langword="false"/> when no live subscription has the id.</returns>
    public bool Remove(uint id)
    {
        long released;
        lock (gate)
        {
            if (!subscriptions.TryGetValue(id, out LocalSubscription? subscription) || subscription.Lapsed)
                return false;
            released = Drop(subscription);
        }

        Release(released);
        return true;
    }

    /// <summary>Withdraws a subscription that was added but whose <c>Subscribe</c> then failed.</summary>
    /// <param name="subscription">The subscription.</param>
    public void Withdraw(LocalSubscription subscription)
    {
        long released;
        lock (gate)
        {
            if (!subscriptions.TryGetValue(subscription.Id, out LocalSubscription? held) || !ReferenceEquals(held, subscription))
                return;
            released = Drop(subscription);
        }

        Release(released);
    }

    /// <summary>The <c>Response</c> to its <c>Subscribe</c> was written: its events may follow.</summary>
    /// <param name="subscription">The subscription.</param>
    public void Answered(LocalSubscription subscription)
    {
        lock (gate)
            subscription.Answered = true;
        Wake();
    }

    /// <summary>The subscriptions of a topic now, for the event hub.</summary>
    /// <param name="topic">The topic.</param>
    /// <returns>A copy; empty when none.</returns>
    public LocalSubscription[] Of(Topic topic)
    {
        lock (gate)
        {
            if (order.Count == 0)
                return [];
            var found = new List<LocalSubscription>();
            foreach (LocalSubscription subscription in order)
            {
                if (subscription.Topic == topic && !subscription.Lapsed)
                    found.Add(subscription);
            }

            return [.. found];
        }
    }

    /// <summary>Offers an event to a subscription, under the bounds of §7.4.</summary>
    /// <param name="subscription">The subscription.</param>
    /// <param name="queued">The event and its frame's size.</param>
    /// <returns>What happened.</returns>
    public OfferResult Offer(LocalSubscription subscription, QueuedEvent queued)
    {
        long added;
        OfferResult result;
        lock (gate)
        {
            if (closed || subscription.Lapsed || !subscriptions.TryGetValue(subscription.Id, out LocalSubscription? live) || !ReferenceEquals(live, subscription))
                return OfferResult.Ignored;
            if (subscription.Topic == Topic.Entries && queued.StoreSeq <= subscription.LiveAfter)
                return OfferResult.Ignored;

            if (subscription.Queue.Count + 1 > Limits.MaxQueuedEvents || subscription.QueuedBytes + queued.Size > Limits.MaxQueuedEventBytes)
            {
                added = Lapse(subscription);
                result = OfferResult.Lapsed;
            }
            else
            {
                subscription.Queue.Enqueue(queued);
                subscription.QueuedBytes += queued.Size;
                added = queued.Size;
                result = OfferResult.Queued;
            }
        }

        bool fits = Hold(added);
        Wake();
        return fits ? result : OfferResult.Overflow;
    }

    /// <summary>Lapses a subscription that may have missed events (the hub fell behind the writer, §7.4).</summary>
    /// <param name="subscription">The subscription.</param>
    public void LapseNow(LocalSubscription subscription)
    {
        long added;
        lock (gate)
        {
            if (closed || subscription.Lapsed || !subscriptions.ContainsKey(subscription.Id))
                return;
            added = Lapse(subscription);
        }

        Hold(added);
        Wake();
    }

    /// <summary>Waits for the next thing to write: a control frame first, then the subscriptions round robin.</summary>
    /// <param name="cancellationToken">Ends the wait when the connection ends.</param>
    /// <returns>The work.</returns>
    public async ValueTask<OutWork> TakeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryTake(out OutWork work))
                return work;
            await wake.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A replay step read an event: it is held while being written.</summary>
    /// <param name="size">The event frame's size.</param>
    /// <returns><see langword="false"/> when the connection now holds more than 64 MiB.</returns>
    public bool HoldReplayed(long size) => Hold(size);

    /// <summary>A frame was written: its bytes are released, and a written <c>SubscriptionLapsed</c> frees its id (§7.1).</summary>
    /// <param name="work">What was written.</param>
    /// <param name="size">Its size.</param>
    public void Written(OutWork work, long size)
    {
        if (work.Subscription is { } subscription && work.Event.Event is SubscriptionLapsed)
        {
            lock (gate)
            {
                if (subscriptions.TryGetValue(subscription.Id, out LocalSubscription? held) && ReferenceEquals(held, subscription))
                {
                    subscriptions.Remove(subscription.Id);
                    order.Remove(subscription);
                }
            }
        }

        Release(size);
    }

    /// <summary>The connection ends: nothing more is queued, and everything held is released.</summary>
    public void Close()
    {
        lock (gate)
        {
            if (closed)
                return;
            closed = true;
            control.Clear();
            subscriptions.Clear();
            order.Clear();
        }

        Volatile.Write(ref drained, 1);
        account(-Interlocked.Exchange(ref held, 0));
        wake.Writer.TryComplete();
    }

    private bool TryTake(out OutWork work)
    {
        lock (gate)
        {
            if (control.TryDequeue(out OutFrame? frame))
            {
                work = new OutWork(frame, null, default, 0);
                return true;
            }

            for (int i = 0; i < order.Count; i++)
            {
                LocalSubscription subscription = order[(cursor + i) % order.Count];
                if (!subscription.HasWork)
                    continue;
                cursor = (cursor + i + 1) % order.Count;
                if (subscription.Queue.Count == 0 || (!subscription.Lapsed && subscription.Replay.Count != 0))
                {
                    work = new OutWork(null, subscription, default, subscription.Replay.Dequeue());
                    return true;
                }

                QueuedEvent queued = subscription.Queue.Dequeue();
                subscription.QueuedBytes -= queued.Size;
                work = new OutWork(null, subscription, queued, 0);
                return true;
            }
        }

        work = default;
        return false;
    }

    // Discards a subscription's queue and puts one SubscriptionLapsed in its place (§7.4); returns the bytes added.
    private static long Lapse(LocalSubscription subscription)
    {
        uint dropped = (uint)subscription.Queue.Count + 1;
        long released = subscription.QueuedBytes;
        subscription.Queue.Clear();
        subscription.Replay.Clear();
        subscription.Lapsed = true;
        var lapsed = new SubscriptionLapsed
        {
            Dropped = dropped,
            LastSeq = subscription.Topic == Topic.Entries ? subscription.LastSeqHanded : 0,
        };
        long size = FrameSizes.OfEvent(lapsed);
        subscription.Queue.Enqueue(new QueuedEvent(lapsed, size, 0));
        subscription.QueuedBytes = size;
        return size - released;
    }

    private long Drop(LocalSubscription subscription)
    {
        long released = subscription.QueuedBytes;
        subscription.Queue.Clear();
        subscription.Replay.Clear();
        subscription.QueuedBytes = 0;
        subscription.Lapsed = true;
        subscriptions.Remove(subscription.Id);
        order.Remove(subscription);
        return released;
    }

    private bool Hold(long bytes)
    {
        if (Volatile.Read(ref drained) != 0)
            return false;
        long now = Interlocked.Add(ref held, bytes);
        account(bytes);
        return now <= Limits.MaxUnacceptedBytesPerConnection;
    }

    private void Release(long bytes)
    {
        if (bytes == 0 || Volatile.Read(ref drained) != 0)
            return;
        Interlocked.Add(ref held, -bytes);
        account(-bytes);
    }

    private void Wake() => wake.Writer.TryWrite(true);
}
