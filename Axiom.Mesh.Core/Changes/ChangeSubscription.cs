using System;
using System.Threading.Channels;

namespace Axiom.Mesh.Core;

/// <summary>
/// One subscriber of the <see cref="ChangeBus"/>: a bounded queue of change sets. When the queue is full the
/// subscription lapses: its reader completes, <see cref="Lapsed"/> is set, and the subscriber re-reads the state it
/// tracks and subscribes again.
/// </summary>
internal sealed class ChangeSubscription : IDisposable
{
    private readonly ChangeBus bus;
    private readonly Channel<ChangeSet> queue;

    internal ChangeSubscription(ChangeBus bus, int capacity)
    {
        this.bus = bus;
        queue = Channel.CreateBounded<ChangeSet>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    /// <summary>The change sets, in the order the writer committed them; completes when the subscription lapses or ends.</summary>
    public ChannelReader<ChangeSet> Reader => queue.Reader;

    /// <summary>Whether a change set was dropped because the queue was full.</summary>
    public bool Lapsed { get; private set; }

    /// <summary>Leaves the bus; the reader completes.</summary>
    public void Dispose()
    {
        bus.Remove(this);
        queue.Writer.TryComplete();
    }

    internal void Offer(ChangeSet changes)
    {
        if (queue.Writer.TryWrite(changes))
            return;

        if (!Lapsed)
        {
            Lapsed = true;
            bus.Remove(this);
            queue.Writer.TryComplete();
        }
    }
}
