using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Discovery;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The static peers of a node (SPEC.md §3.3; LOCAL-SPEC.md §6.6): the addresses the configuration lists, plus the ones
/// <c>AddStaticPeer</c> added, which the index keeps across restarts. Every address is offered to the peer table at start,
/// after every change and once a minute; a host name is resolved on every round.
/// </summary>
internal sealed class StaticPeerBook
{
    private readonly SemaphoreSlim one = new(1, 1);
    private readonly SemaphoreSlim changed = new(0, int.MaxValue);
    private readonly object gate = new();
    private readonly HashSet<string> configured;
    private readonly StoreWriter writer;
    private readonly PeerTable table;
    private readonly TimeProvider time;
    private readonly Dictionary<string, IPEndPoint[]> resolved = new(StringComparer.Ordinal);
    private List<string> added;

    private StaticPeerBook(IEnumerable<string> configured, List<string> added, StoreWriter writer, PeerTable table, TimeProvider time)
    {
        this.configured = new HashSet<string>(configured, StringComparer.Ordinal);
        this.added = added;
        this.writer = writer;
        this.table = table;
        this.time = time;
    }

    /// <summary>Every static peer, ascending by address as byte strings.</summary>
    public IReadOnlyList<string> All
    {
        get
        {
            lock (gate)
                return [.. configured.Concat(added).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>Opens the book: the configured addresses and those the index kept.</summary>
    /// <param name="configured">The configuration's addresses.</param>
    /// <param name="writer">The store writer, whose index keeps the added addresses.</param>
    /// <param name="table">The peer table the addresses are offered to.</param>
    /// <param name="time">The node's clock.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The book.</returns>
    public static async Task<StaticPeerBook> OpenAsync(IReadOnlyList<string> configured, StoreWriter writer, PeerTable table, TimeProvider time, CancellationToken cancellationToken)
    {
        await using LdbContext context = await LdbContext.OpenAsync(writer.Database, cancellationToken).ConfigureAwait(false);
        MetaRow? row = await context.Set<MetaRow>().FindAsync(MetaKeys.StaticPeers, cancellationToken).ConfigureAwait(false);
        List<string> kept = row?.Bytes is { Length: > 0 } bytes ? [.. Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries)] : [];
        return new StaticPeerBook(configured, kept, writer, table, time);
    }

    /// <summary>The <c>StaticPeer</c> record of an address (§5.9).</summary>
    /// <param name="address">The address as configured.</param>
    /// <returns>The record.</returns>
    public StaticPeer RecordOf(string address)
    {
        IPEndPoint[] targets;
        lock (gate)
            targets = resolved.TryGetValue(address, out IPEndPoint[]? known) ? known : [];
        (DateTimeOffset? at, DialResult result) last = (null, DialResult.NeverDialled);
        foreach (IPEndPoint target in targets)
        {
            (DateTimeOffset? at, DialResult result) status = table.StaticStatus(target);
            if (status.at is { } dialled && (last.at is null || dialled > last.at))
                last = status;
        }

        return new StaticPeer { Address = address, LastDialled = LocalRecords.Milliseconds(last.at), LastResult = last.result };
    }

    /// <summary><c>AddStaticPeer</c>: kept in the index and offered at once.</summary>
    /// <param name="address">The address, <c>host:port</c>.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns>Why it was not added, or <see langword="null"/>.</returns>
    public async Task<RequestRefusal?> AddAsync(string address, CancellationToken cancellationToken)
    {
        if (!StaticPeers.TryParseAddress(address, out _))
            return new RequestRefusal(Status.Invalid, $"\"{address}\" is not host:port.", "address");
        await one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<string> all = All;
            if (all.Contains(address, StringComparer.Ordinal))
                return new RequestRefusal(Status.AlreadyExists, $"{address} is a static peer already.", "address");
            if (all.Count >= Limits.MaxStaticPeers)
                return new RequestRefusal(Status.Busy, $"{Limits.MaxStaticPeers} static peers are configured; remove one first.", string.Empty);
            List<string> next = [.. added, address];
            await writer.SendAsync(new StaticPeersCommand(next), cancellationToken).ConfigureAwait(false);
            lock (gate)
                added = next;
        }
        finally
        {
            one.Release();
        }

        changed.Release();
        return null;
    }

    /// <summary><c>RemoveStaticPeer</c>: no longer kept or dialled. A configured address is the configuration's to remove.</summary>
    /// <param name="address">The address as added.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns>Why it was not removed, or <see langword="null"/>.</returns>
    public async Task<RequestRefusal?> RemoveAsync(string address, CancellationToken cancellationToken)
    {
        await one.WaitAsync(cancellationToken).ConfigureAwait(false);
        IPEndPoint[] targets;
        try
        {
            if (configured.Contains(address))
                return new RequestRefusal(Status.Invalid, $"{address} is listed in the Host's configuration (mesh.staticPeers); remove it there.", "address");
            if (!added.Contains(address, StringComparer.Ordinal))
                return new RequestRefusal(Status.NotFound, $"{address} is not a static peer.", "address");
            List<string> next = [.. added.Where(a => !string.Equals(a, address, StringComparison.Ordinal))];
            await writer.SendAsync(new StaticPeersCommand(next), cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                added = next;
                resolved.Remove(address, out IPEndPoint[]? known);
                targets = known ?? [];
            }
        }
        finally
        {
            one.Release();
        }

        foreach (IPEndPoint target in targets)
            table.ForgetStatic(target);
        return null;
    }

    /// <summary>Offers every address to the peer table, at once, after every change and once a minute, until stopped.</summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when stopped.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var facts = new DiscoveryFacts(16);
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (string address in All)
            {
                if (!StaticPeers.TryParseAddress(address, out EndPoint? endPoint))
                    continue;
                var once = new StaticPeers(new StaticPeersOptions { Peers = [endPoint], Time = time, Facts = facts, Interval = Timeout.InfiniteTimeSpan });
                var targets = new List<IPEndPoint>();
                await foreach (Announcement announcement in once.ObserveAsync(cancellationToken).ConfigureAwait(false))
                {
                    targets.Add(announcement.Address);
                    table.Offer(announcement);
                }

                lock (gate)
                    resolved[address] = [.. targets];
            }

            try
            {
                // With no static peer there is nothing to offer again: only a change wakes the loop.
                using var round = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task due = All.Count == 0 ? Task.Delay(Timeout.InfiniteTimeSpan, round.Token) : Task.Delay(StaticPeersOptions.DefaultInterval, time, round.Token);
                await Task.WhenAny(due, changed.WaitAsync(round.Token)).ConfigureAwait(false);
                await round.CancelAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
