using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// The observers of one shell: each gets its own bounded queue, so a slow observer drops its own oldest announcements
/// and never blocks the receive loop or another observer.
/// </summary>
internal sealed class Subscribers
{
    /// <summary>Announcements an observer may fall behind by before its oldest are dropped.</summary>
    public const int QueueLength = 256;

    private readonly List<Channel<Announcement>> channels = [];
    private readonly object sync = new();
    private bool completed;

    /// <summary>Hands an announcement to every observer. Never blocks.</summary>
    /// <param name="announcement">What was heard.</param>
    public void Publish(Announcement announcement)
    {
        lock (sync)
        {
            foreach (Channel<Announcement> channel in channels)
                channel.Writer.TryWrite(announcement);
        }
    }

    /// <summary>Ends every observer's stream; later observers end at once.</summary>
    public void Complete()
    {
        lock (sync)
        {
            completed = true;
            foreach (Channel<Announcement> channel in channels)
                channel.Writer.TryComplete();
            channels.Clear();
        }
    }

    /// <summary>Streams what is published from now on, until cancelled or completed.</summary>
    /// <param name="cancellationToken">Ends the stream.</param>
    /// <returns>The announcements.</returns>
    public async IAsyncEnumerable<Announcement> ObserveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<Announcement> channel = Channel.CreateBounded<Announcement>(new BoundedChannelOptions(QueueLength)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        lock (sync)
        {
            if (completed)
                channel.Writer.TryComplete();
            else
                channels.Add(channel);
        }

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out Announcement? announcement))
                    yield return announcement;
            }
        }
        finally
        {
            lock (sync)
                channels.Remove(channel);
        }
    }
}
