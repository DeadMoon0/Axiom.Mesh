using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// Several mechanisms as one: the announcements of every part, merged through an <see cref="AnnouncementCache"/> so
/// that the same claim heard by mDNS and by broadcast, or heard again within the cache's repeat window, is passed on
/// once. Announcing asks every part to announce.
/// </summary>
/// <remarks>
/// The cache decides what is new, so one observer runs at a time; a second concurrent call to
/// <see cref="ObserveAsync"/> is refused. The engine keeps the cache to look up a device that becomes known later.
/// </remarks>
public sealed class CompositeDiscovery : IDiscovery
{
    private readonly CompositeDiscoveryOptions options;
    private readonly long started;
    private int observing;

    /// <summary>Merges the parts; nothing runs until somebody observes.</summary>
    /// <param name="options">The parts, the clock and the cache.</param>
    public CompositeDiscovery(CompositeDiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        started = options.Time.GetTimestamp();
    }

    /// <summary>The cache that decides what is new: the newest claim of every device heard, bounded.</summary>
    public AnnouncementCache Cache => options.Cache;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Another observer is running.</exception>
    public IAsyncEnumerable<Announcement> ObserveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref observing, 1, 0) != 0)
            throw new InvalidOperationException("A CompositeDiscovery has one observer at a time: its cache decides what is new. Observe it once and fan the stream out.");

        return MergeAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask AnnounceAsync(CancellationToken cancellationToken)
    {
        foreach (IDiscovery part in options.Parts)
            await part.AnnounceAsync(cancellationToken).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<Announcement> MergeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Channel<Announcement> merged = Channel.CreateBounded<Announcement>(new BoundedChannelOptions(Subscribers.QueueLength)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var pumps = new List<Task>(options.Parts.Count);
        foreach (IDiscovery part in options.Parts)
            pumps.Add(PumpAsync(part, merged.Writer, stop.Token));
        _ = CompleteWhenAllEndAsync(pumps, merged.Writer);

        try
        {
            while (await merged.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (merged.Reader.TryRead(out Announcement? announcement))
                {
                    if (options.Cache.Observe(announcement, options.Time.GetElapsedTime(started)) != AnnouncementNovelty.Duplicate)
                        yield return announcement;
                }
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(pumps).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            Interlocked.Exchange(ref observing, 0);
        }
    }

    private static async Task PumpAsync(IDiscovery part, ChannelWriter<Announcement> merged, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (Announcement announcement in part.ObserveAsync(cancellationToken).ConfigureAwait(false))
                merged.TryWrite(announcement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    // The merged stream ends when every part's stream has ended (all disposed, or all finite and done).
    private static async Task CompleteWhenAllEndAsync(List<Task> pumps, ChannelWriter<Announcement> merged)
    {
        try
        {
            await Task.WhenAll(pumps).ConfigureAwait(false);
            merged.TryComplete();
        }
        catch (Exception e)
        {
            merged.TryComplete(e);
        }
    }
}
