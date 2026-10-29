using System;
using System.Threading.Channels;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// Where discovery shells record what they could not do. Bounded: when nobody reads, the oldest facts are dropped and
/// recording never blocks a shell. The engine reads <see cref="Reader"/> and logs; discovery itself logs nothing.
/// </summary>
public sealed class DiscoveryFacts
{
    private readonly Channel<DiscoveryFact> channel;

    /// <summary>Creates a sink that keeps at most <paramref name="capacity"/> unread facts.</summary>
    /// <param name="capacity">Unread facts kept; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is below 1.</exception>
    public DiscoveryFacts(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Keep at least one fact.");

        Capacity = capacity;
        channel = Channel.CreateBounded<DiscoveryFact>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    /// <summary>The number of unread facts kept before the oldest is dropped.</summary>
    public int Capacity { get; }

    /// <summary>The recorded facts, oldest first.</summary>
    public ChannelReader<DiscoveryFact> Reader => channel.Reader;

    /// <summary>Records a fact; drops the oldest unread one when full. Never blocks.</summary>
    /// <param name="fact">What could not be done.</param>
    public void Record(DiscoveryFact fact) => channel.Writer.TryWrite(fact);
}
