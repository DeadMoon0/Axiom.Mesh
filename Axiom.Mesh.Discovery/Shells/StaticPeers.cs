using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// The addresses the user configured (SPEC.md §3.3), offered to the dialler on start and then on every interval, as
/// announcements of <see cref="DiscoverySource.Static"/> with all-zero ids: a static peer claims nothing, and the TLS
/// handshake decides whether the node there is acceptable. A host name is resolved on every round; one that does not
/// resolve is recorded and tried again.
/// </summary>
public sealed class StaticPeers : IDiscovery
{
    /// <summary>The most addresses taken from one host name.</summary>
    public const int MaxAddressesPerName = 8;

    private readonly StaticPeersOptions options;

    /// <summary>Holds the configured list; nothing runs until somebody observes.</summary>
    /// <param name="options">The peers and the interval.</param>
    /// <exception cref="ArgumentException">A peer is neither an <see cref="IPEndPoint"/> nor a <see cref="DnsEndPoint"/>, or names port 0.</exception>
    public StaticPeers(StaticPeersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Intervals.Check(options.Interval, nameof(options.Interval));
        foreach (EndPoint peer in options.Peers)
        {
            int port = peer switch
            {
                IPEndPoint address => address.Port,
                DnsEndPoint name => name.Port,
                _ => throw new ArgumentException($"A static peer is an IP address or a host name with a port; {peer} is a {peer.GetType().Name}.", nameof(options)),
            };
            if (port == 0)
                throw new ArgumentException($"The static peer {peer} names port 0, which cannot be dialled; the mesh port is 41810 by default.", nameof(options));
        }

        this.options = options;
    }

    /// <summary>
    /// Reads a static peer's address as the configuration and the local protocol write it (SPEC.md §3.3): <c>host:port</c>
    /// with an IP address (IPv6 in brackets) or a host name, and a port from 1 to 65535.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="address">An <see cref="IPEndPoint"/> or a <see cref="DnsEndPoint"/>.</param>
    /// <returns><see langword="true"/> when the text is such an address.</returns>
    public static bool TryParseAddress(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EndPoint? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (IPEndPoint.TryParse(text, out IPEndPoint? ip) && ip.Port != 0 && text.Contains(':', StringComparison.Ordinal))
        {
            address = ip;
            return true;
        }

        int colon = text.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(text.AsSpan(colon + 1), out int port) || port is < 1 or > 65535)
            return false;
        string host = text[..colon];
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            return false;
        address = new DnsEndPoint(host, port);
        return true;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Announcement> ObserveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            foreach (EndPoint peer in options.Peers)
            {
                if (peer is IPEndPoint address)
                {
                    yield return Announcement.ForStaticPeer(address);
                    continue;
                }

                foreach (IPAddress resolved in await ResolveAsync((DnsEndPoint)peer, cancellationToken).ConfigureAwait(false))
                    yield return Announcement.ForStaticPeer(new IPEndPoint(resolved, ((DnsEndPoint)peer).Port));
            }

            if (options.Interval == Timeout.InfiniteTimeSpan)
                yield break;
            await Task.Delay(options.Interval, options.Time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Does nothing: a static peer list announces nobody.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public ValueTask AnnounceAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    private async Task<IReadOnlyList<IPAddress>> ResolveAsync(DnsEndPoint peer, CancellationToken cancellationToken)
    {
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(peer.Host, peer.AddressFamily, cancellationToken).ConfigureAwait(false);
            return addresses.Length > MaxAddressesPerName ? addresses[..MaxAddressesPerName] : addresses;
        }
        catch (SocketException e)
        {
            options.Facts.Record(new DiscoveryFact
            {
                At = options.Time.GetUtcNow(),
                Mechanism = DiscoverySource.Static,
                Kind = DiscoveryFactKind.NameNotResolved,
                Subject = peer.Host,
                Detail = e.SocketErrorCode.ToString(),
            });
            return [];
        }
    }
}
