using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// One way of finding nodes on the local network (SPEC.md §3), or several merged. Everything it reports is a hint;
/// what it could not do (a socket that would not bind, an interface that went away) is recorded as a
/// <see cref="DiscoveryFact"/> in the <see cref="DiscoveryFacts"/> it was given, never thrown.
/// </summary>
public interface IDiscovery
{
    /// <summary>
    /// Streams the announcements heard from now on, until <paramref name="cancellationToken"/> is cancelled or the
    /// discovery is disposed. On a shell several observers may run at once, each receiving every announcement; a
    /// <see cref="CompositeDiscovery"/>, whose cache decides what is new, takes one at a time. A slow observer loses the
    /// oldest announcements it has not read; it never blocks the receiver. Cancelling ends the stream with an
    /// <see cref="System.OperationCanceledException"/>.
    /// </summary>
    /// <param name="cancellationToken">Ends the stream.</param>
    /// <returns>The announcements, as they arrive.</returns>
    IAsyncEnumerable<Announcement> ObserveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Announces this node now on every interface the announcement gate lets through (at most one announcement per
    /// second per interface, SPEC.md §3.2). A failure on one interface is recorded and the others are still tried.
    /// </summary>
    /// <param name="cancellationToken">Stops announcing.</param>
    /// <returns>A task that completes when every interface was tried.</returns>
    ValueTask AnnounceAsync(CancellationToken cancellationToken);
}
