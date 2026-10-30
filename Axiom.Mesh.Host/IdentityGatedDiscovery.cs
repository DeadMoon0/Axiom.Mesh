using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Axiom.Mesh.Discovery;

namespace Axiom.Mesh.Host;

/// <summary>
/// mDNS and broadcast discovery (SPEC.md §3.1, §3.2) as the daemon runs them: both announce this device, so they open
/// only once the device has an identity, which a fresh daemon gets from <c>CreateIdentity</c> while it runs. Until then
/// it hears nothing and announces nothing; static peers (§3.3) need no identity and run beside it from the start.
/// </summary>
internal sealed class IdentityGatedDiscovery : IDiscovery, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<SelfRecord>> self;
    private readonly bool mdns;
    private readonly bool broadcast;
    private readonly TimeProvider time;
    private readonly DiscoveryFacts facts;
    private readonly List<IDiscovery> shells = [];
    private readonly object gate = new();

    /// <summary>Discovery that waits for the device's identity.</summary>
    /// <param name="self">Completes with this device's record once it has an identity and its mesh port is bound.</param>
    /// <param name="mdns">Whether to run mDNS/DNS-SD.</param>
    /// <param name="broadcast">Whether to run the broadcast port.</param>
    /// <param name="time">The daemon's clock.</param>
    /// <param name="facts">Where the shells report what they did.</param>
    public IdentityGatedDiscovery(Func<CancellationToken, Task<SelfRecord>> self, bool mdns, bool broadcast, TimeProvider time, DiscoveryFacts facts)
    {
        this.self = self;
        this.mdns = mdns;
        this.broadcast = broadcast;
        this.time = time;
        this.facts = facts;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Announcement> ObserveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!mdns && !broadcast)
            yield break;

        SelfRecord record = await self(cancellationToken).ConfigureAwait(false);
        IDiscovery[] opened = Open(record);
        var merged = Channel.CreateBounded<Announcement>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        Task[] pumps = Array.ConvertAll(opened, shell => PumpAsync(shell, merged.Writer, cancellationToken));
        _ = Task.WhenAll(pumps).ContinueWith(_ => merged.Writer.TryComplete(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        foreach (IDiscovery shell in opened)
            await shell.AnnounceAsync(cancellationToken).ConfigureAwait(false);
        await foreach (Announcement announcement in merged.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return announcement;
    }

    /// <inheritdoc />
    public async ValueTask AnnounceAsync(CancellationToken cancellationToken)
    {
        IDiscovery[] opened;
        lock (gate)
            opened = [.. shells];
        foreach (IDiscovery shell in opened)
            await shell.AnnounceAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        IDiscovery[] opened;
        lock (gate)
        {
            opened = [.. shells];
            shells.Clear();
        }

        foreach (IDiscovery shell in opened)
        {
            if (shell is IAsyncDisposable disposable)
                await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    private IDiscovery[] Open(SelfRecord record)
    {
        lock (gate)
        {
            if (mdns)
                shells.Add(MdnsDiscovery.Open(new MdnsDiscoveryOptions { Self = record, Time = time, Facts = facts }));
            if (broadcast)
                shells.Add(BroadcastDiscovery.Open(new BroadcastDiscoveryOptions { Self = record, Time = time, Facts = facts }));
            return [.. shells];
        }
    }

    private static async Task PumpAsync(IDiscovery shell, ChannelWriter<Announcement> merged, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (Announcement announcement in shell.ObserveAsync(cancellationToken).ConfigureAwait(false))
                merged.TryWrite(announcement);
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }
}
