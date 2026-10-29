using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Axiom.Mesh.Discovery;

namespace Axiom.Mesh.Testing;

/// <summary>
/// One node's discovery on an <see cref="InMemoryNetwork"/>'s board: what mDNS and broadcast would be on a LAN. It
/// announces the node's claim on the board and hears every other node's claim on its side of a partition, the ones
/// already on the board first. Announcements carry <see cref="DiscoverySource.Mdns"/>.
/// </summary>
public sealed class FakeDiscovery : IDiscovery
{
    private readonly InMemoryNetwork network;

    /// <summary>The discovery of one node.</summary>
    /// <param name="network">The board.</param>
    /// <param name="self">The node's own claim: its device, user and mesh endpoint.</param>
    public FakeDiscovery(InMemoryNetwork network, Announcement self)
    {
        this.network = network ?? throw new ArgumentNullException(nameof(network));
        Self = self ?? throw new ArgumentNullException(nameof(self));
    }

    /// <summary>The node's own claim.</summary>
    public Announcement Self { get; }

    /// <summary>Puts the node's claim on the board now.</summary>
    public void Announce() => network.Announce(Self);

    /// <summary>Takes the node's claim off the board, as a node that left the network.</summary>
    public void Withdraw() => network.Withdraw(Self.Device);

    /// <summary>
    /// Whether the node keeps quiet: its own announcements (when it starts) are dropped, so it dials but is never dialled.
    /// <see cref="Announce"/> still puts the claim on the board when a test calls it.
    /// </summary>
    public bool IsSilent { get; set; }

    /// <inheritdoc />
    public ValueTask AnnounceAsync(CancellationToken cancellationToken)
    {
        if (!IsSilent)
            Announce();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Announcement> ObserveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<Announcement> queue = network.Join(Self);
        try
        {
            await foreach (Announcement announcement in queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return announcement;
        }
        finally
        {
            network.Leave(queue);
        }
    }
}
