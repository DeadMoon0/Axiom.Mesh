using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Discovery;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The infrastructure requests of LOCAL-SPEC.md the node answers from its own state: <c>GetStore</c> (§6.5),
/// <c>ListServices</c> (§6.7), <c>ListPeers</c>, <c>ListSessions</c> and <c>ListDiscovered</c> (§6.6).
/// </summary>
internal static class NodeRequests
{
    private const int MaxPeers = 1024;
    private const int MaxSessions = 256;
    private const int MaxServices = 64;

    /// <summary><c>GetStore</c> (355): the store and its budget.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> GetStoreAsync(RequestContext context, uint correlation, GetStore request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        Store store = LocalRecords.StoreOf(node.Writer.Totals, node.Files.StorePath, node.Replication.HeldBytes);
        return ValueTask.FromResult(LocalAnswer.Ok<GetStore, Store>(correlation, store));
    }

    /// <summary><c>ListServices</c> (384): every configured service, ascending by name.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> ListServicesAsync(RequestContext context, uint correlation, ListServices request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Service> services = [.. context.Port.Options.Services
            .OrderBy(s => s.Name, System.StringComparer.Ordinal)
            .Take(MaxServices)
            .Select(s => LocalRecords.ServiceOf(s, context.Port.Book.ServiceConnection(s.Name)?.Hello?.ClientVersion))];
        return ValueTask.FromResult(LocalAnswer.Ok<ListServices, IReadOnlyList<Service>>(correlation, services));
    }

    /// <summary><c>ListPeers</c> (368): the known peers, ascending by device id.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> ListPeersAsync(RequestContext context, uint correlation, ListPeers request, CancellationToken cancellationToken)
    {
        TrustSnapshot trust = context.Node.Trust;
        IReadOnlyList<Peer> peers = [.. context.Node.Table.View
            .Select(p => LocalRecords.PeerOf(p, trust))
            .Where(p => p.Known)
            .OrderBy(p => p.DeviceId.Value)
            .Take(MaxPeers)];
        return ValueTask.FromResult(LocalAnswer.Ok<ListPeers, IReadOnlyList<Peer>>(correlation, peers));
    }

    /// <summary><c>ListSessions</c> (369): the established sessions, ascending by device id.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> ListSessionsAsync(RequestContext context, uint correlation, ListSessions request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        System.DateTimeOffset now = node.Time.GetUtcNow();
        IReadOnlyList<Session> sessions = [.. node.Replication.Sessions
            .Select(s => LocalRecords.SessionOf(s, node.Table.EstablishedWith(s.Device), now))
            .OrderBy(s => s.DeviceId.Value)
            .Take(MaxSessions)];
        return ValueTask.FromResult(LocalAnswer.Ok<ListSessions, IReadOnlyList<Session>>(correlation, sessions));
    }

    /// <summary>
    /// <c>ListDiscovered</c> (370): every device discovery reported, known or not, ascending by device id: the peer table's
    /// view and what the discovery cache heard of devices the table does not hold.
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> ListDiscoveredAsync(RequestContext context, uint correlation, ListDiscovered request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        TrustSnapshot trust = node.Trust;
        var peers = node.Table.View.ToDictionary(p => p.Device, p => LocalRecords.PeerOf(p, trust));
        if (node.Discovery is CompositeDiscovery composite)
        {
            foreach (Announcement heard in composite.Cache.Snapshot())
            {
                if (heard.Source != DiscoverySource.Static && !peers.ContainsKey(heard.Device))
                    peers[heard.Device] = LocalRecords.PeerOf(heard, trust);
            }
        }

        IReadOnlyList<Peer> list = [.. peers.Values.OrderBy(p => p.DeviceId.Value).Take(MaxPeers)];
        return ValueTask.FromResult(LocalAnswer.Ok<ListDiscovered, IReadOnlyList<Peer>>(correlation, list));
    }
}
