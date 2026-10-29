using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Discovery;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core;

/// <summary>
/// Feeds the peer table from discovery and from trust (SPEC.md §3, §4.3, §8.8): announces the node once its listener is
/// up, offers every announcement heard to the table, and after every trust change closes the sessions of devices no
/// longer trusted and offers the devices discovery heard before, so that a user who became known is dialled at once.
/// </summary>
internal sealed class DiscoveryPump
{
    private const int TrustBacklog = 64;

    private readonly IDiscovery discovery;
    private readonly PeerTable table;
    private readonly ChangeBus bus;
    private readonly ChangeSubscription first;
    private readonly Func<TrustSnapshot> trust;

    /// <summary>A pump for a node; it subscribes to trust changes at once, so that none is missed before it runs.</summary>
    /// <param name="discovery">The node's discovery: one observer, this pump.</param>
    /// <param name="table">The peer table.</param>
    /// <param name="bus">The store writer's change bus, for trust changes.</param>
    /// <param name="trust">The trust that holds now.</param>
    public DiscoveryPump(IDiscovery discovery, PeerTable table, ChangeBus bus, Func<TrustSnapshot> trust)
    {
        this.discovery = discovery;
        this.table = table;
        this.bus = bus;
        this.trust = trust;
        first = bus.Subscribe(TrustBacklog);
    }

    /// <summary>Announces, then observes discovery until stopped.</summary>
    /// <param name="cancellationToken">Stops observing.</param>
    /// <returns>A task that completes when stopped.</returns>
    public async Task ObserveAsync(CancellationToken cancellationToken)
    {
        await discovery.AnnounceAsync(cancellationToken).ConfigureAwait(false);
        await foreach (Announcement announcement in discovery.ObserveAsync(cancellationToken).ConfigureAwait(false))
            table.Offer(announcement);
    }

    /// <summary>Follows trust changes until stopped.</summary>
    /// <param name="cancellationToken">Stops following.</param>
    /// <returns>A task that completes when stopped.</returns>
    public async Task FollowTrustAsync(CancellationToken cancellationToken)
    {
        ChangeSubscription changes = first;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await foreach (ChangeSet set in changes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (set.TrustChanged)
                        Apply(trust());
                }

                // A lapsed subscription missed changes: take a new one and apply the trust that holds now.
                changes.Dispose();
                changes = bus.Subscribe(TrustBacklog);
                Apply(trust());
            }
        }
        finally
        {
            changes.Dispose();
        }
    }

    // The trust read now, never the change set's own: a set this loop reads late carries a snapshot newer ones replaced,
    // and judging sessions by it would close a device the node trusts again.
    private void Apply(TrustSnapshot current)
    {
        foreach (PeerSession session in table.Untrusted(current))
            session.RequestClose(ByeReason.Unauthorized);
        if (discovery is CompositeDiscovery composite)
        {
            foreach (Announcement heard in composite.Cache.Snapshot())
                table.Offer(heard);
        }
    }
}
