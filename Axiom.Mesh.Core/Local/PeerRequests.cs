using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The static peer and dial requests of LOCAL-SPEC.md §6.6: <c>ListStaticPeers</c>, <c>AddStaticPeer</c>,
/// <c>RemoveStaticPeer</c>, <c>Dial</c>. (<c>ListPeers</c>, <c>ListSessions</c> and <c>ListDiscovered</c> are in
/// <see cref="NodeRequests"/>.)
/// </summary>
internal static class PeerRequests
{
    /// <summary><c>ListStaticPeers</c> (371): every static peer, configured or added, ascending by address.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> ListStaticPeersAsync(RequestContext context, uint correlation, ListStaticPeers request, CancellationToken cancellationToken)
    {
        StaticPeerBook book = context.Node.StaticPeers;
        IReadOnlyList<StaticPeer> peers = [.. book.All.Take(Limits.MaxStaticPeers).Select(book.RecordOf)];
        return ValueTask.FromResult(LocalAnswer.Ok<ListStaticPeers, IReadOnlyList<StaticPeer>>(correlation, peers));
    }

    /// <summary><c>AddStaticPeer</c> (372): kept and dialled; <c>AlreadyExists</c>, <c>Busy</c> at 256, <c>Invalid</c> for no <c>host:port</c>.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> AddStaticPeerAsync(RequestContext context, uint correlation, AddStaticPeer request, CancellationToken cancellationToken)
    {
        StaticPeerBook book = context.Node.StaticPeers;
        if (await book.AddAsync(request.Address, cancellationToken).ConfigureAwait(false) is { } refusal)
            return refusal.Answer(correlation);
        return LocalAnswer.Ok<AddStaticPeer, StaticPeer>(correlation, book.RecordOf(request.Address));
    }

    /// <summary><c>RemoveStaticPeer</c> (373): no longer kept or dialled; <c>NotFound</c> for an address that is not one.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> RemoveStaticPeerAsync(RequestContext context, uint correlation, RemoveStaticPeer request, CancellationToken cancellationToken)
    {
        if (await context.Node.StaticPeers.RemoveAsync(request.Address, cancellationToken).ConfigureAwait(false) is { } refusal)
            return refusal.Answer(correlation);
        return LocalAnswer.Ok<RemoveStaticPeer, NoContent>(correlation, default);
    }

    /// <summary>
    /// <c>Dial</c> (374): starts a dial under SPEC.md §4.4, backoff included; the outcome is an event on topic <c>peers</c>.
    /// <c>Invalid</c> with both fields empty or an address that is no <c>IP:port</c>, <c>NotFound</c> for a device that is
    /// not known or has no address, <c>AlreadyExists</c> for a device with an established session.
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> DialAsync(RequestContext context, uint correlation, Dial request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        if (DialRule.Check(node.Trust, request.DeviceId, request.Address, node.Table.EstablishedWith(request.DeviceId) is not null, out IPEndPoint? address) is { } refusal)
            return ValueTask.FromResult(refusal.Answer(correlation));

        if (request.DeviceId.Value.IsZero)
        {
            node.Table.DialAddress(address!);
        }
        else
        {
            node.Trust.TryGetDevice(request.DeviceId, out TrustedDevice device);
            if (!node.Table.DialDevice(request.DeviceId, device.User, address))
                return ValueTask.FromResult(LocalAnswer.Failure(correlation, Status.NotFound, $"No address is known for device {request.DeviceId}; name one.", "address"));
        }

        return ValueTask.FromResult(LocalAnswer.Ok<Dial, NoContent>(correlation, default));
    }
}
