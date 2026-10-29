using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// Discovery by UDP broadcast on port 41811 (SPEC.md §3.2), for networks that filter multicast. One socket per address
/// family: IPv4 sends a directed broadcast on every interface, IPv6 (which has no broadcast) sends to the link-local
/// all-nodes group <c>ff02::1</c> of every interface. A query is answered with an announcement sent unicast to its
/// source; every announcement passes the <see cref="AnnounceGate"/> first, so a flood of queries costs at most one
/// answer per second per interface.
/// </summary>
/// <remarks>
/// A thin, dishonest shell over <see cref="BroadcastCodec"/>: it binds, receives, sends and records what it could not
/// do as a <see cref="DiscoveryFact"/>. A socket that will not bind leaves that family silent; the node runs on.
/// </remarks>
public sealed class BroadcastDiscovery : IDiscovery, IAsyncDisposable
{
    private const string AllNodes = "ff02::1";

    private readonly BroadcastDiscoveryOptions options;
    private readonly List<Socket> sockets = [];
    private readonly List<Task> loops = [];
    private readonly Subscribers subscribers = new();
    private readonly AnnounceGate gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly long started;

    private BroadcastDiscovery(BroadcastDiscoveryOptions options)
    {
        this.options = options;
        started = options.Time.GetTimestamp();
    }

    /// <summary>The addresses the sockets are bound to; empty when none could be.</summary>
    public IReadOnlyList<IPEndPoint> LocalEndPoints { get; private set; } = [];

    /// <summary>Binds the sockets and starts receiving (and, unless the interval is infinite, announcing).</summary>
    /// <param name="options">How to run.</param>
    /// <returns>The running discovery; dispose it to stop.</returns>
    public static BroadcastDiscovery Open(BroadcastDiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Intervals.Check(options.AnnounceInterval, nameof(options.AnnounceInterval));
        if (options.Port is < 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options), options.Port, "A UDP port is 0 to 65535.");
        var discovery = new BroadcastDiscovery(options);
        discovery.Start();
        return discovery;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Announcement> ObserveAsync(CancellationToken cancellationToken) => subscribers.ObserveAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask AnnounceAsync(CancellationToken cancellationToken) => SendToEveryInterfaceAsync(BroadcastKind.Announcement, cancellationToken);

    /// <summary>Asks every node on every interface to answer with an announcement. Queries are not gated.</summary>
    /// <param name="cancellationToken">Stops sending.</param>
    /// <returns>A task that completes when every interface was tried.</returns>
    public ValueTask QueryAsync(CancellationToken cancellationToken) => SendToEveryInterfaceAsync(BroadcastKind.Query, cancellationToken);

    /// <summary>Stops receiving and announcing, closes the sockets and ends every observer's stream.</summary>
    /// <returns>A task that completes when the loops have ended.</returns>
    public async ValueTask DisposeAsync()
    {
        if (stopping.IsCancellationRequested)
            return;

        await stopping.CancelAsync().ConfigureAwait(false);
        foreach (Socket socket in sockets)
            socket.Dispose();
        await Task.WhenAll(loops).ConfigureAwait(false);
        subscribers.Complete();
        stopping.Dispose();
    }

    private TimeSpan Now => options.Time.GetElapsedTime(started);

    private void Start()
    {
        bool any = options.BindAddress.Equals(IPAddress.Any);
        if (any || options.BindAddress.AddressFamily == AddressFamily.InterNetwork)
            Bind(any ? IPAddress.Any : options.BindAddress);
        if (any || options.BindAddress.AddressFamily == AddressFamily.InterNetworkV6)
            Bind(any ? IPAddress.IPv6Any : options.BindAddress);

        var bound = new List<IPEndPoint>();
        foreach (Socket socket in sockets)
        {
            bound.Add((IPEndPoint)socket.LocalEndPoint!);
            loops.Add(ReceiveAsync(socket, stopping.Token));
        }

        LocalEndPoints = bound;
        if (options.AnnounceInterval != Timeout.InfiniteTimeSpan)
            loops.Add(AnnounceEveryAsync(options.AnnounceInterval, stopping.Token));
    }

    private void Bind(IPAddress address)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = false;
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.PacketInformation, true);
            }
            else
            {
                socket.EnableBroadcast = true;
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            }

            socket.Bind(new IPEndPoint(address, options.Port));
            sockets.Add(socket);
        }
        catch (SocketException e)
        {
            socket.Dispose();
            Record(DiscoveryFactKind.SocketUnavailable, $"udp {address}:{options.Port}", e.SocketErrorCode.ToString());
        }
    }

    private async Task AnnounceEveryAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            await QueryAsync(cancellationToken).ConfigureAwait(false);
            while (true)
            {
                await AnnounceAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(interval, options.Time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReceiveAsync(Socket socket, CancellationToken cancellationToken)
    {
        // One byte more than a datagram, so a longer one is seen as longer and refused.
        byte[] buffer = new byte[BroadcastCodec.Length + 1];
        EndPoint anyRemote = new IPEndPoint(socket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0);
        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveMessageFromResult received;
            try
            {
                received = await socket.ReceiveMessageFromAsync(buffer, SocketFlags.None, anyRemote, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (cancellationToken.IsCancellationRequested && e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
            {
                // An ICMP error for an earlier send (Windows), or a datagram longer than the buffer: neither is ours.
                continue;
            }
            catch (SocketException e)
            {
                Record(DiscoveryFactKind.ReceiveStopped, $"udp {socket.LocalEndPoint}", e.SocketErrorCode.ToString());
                return;
            }

            if (!BroadcastCodec.TryParse(buffer.AsSpan(0, received.ReceivedBytes), out BroadcastDatagram datagram) || datagram.Device == options.Self.Device)
                continue;

            var remote = (IPEndPoint)received.RemoteEndPoint;
            subscribers.Publish(datagram.ToAnnouncement(remote.Address));
            if (datagram.Kind == BroadcastKind.Query && gate.TryPass(Now, received.PacketInformation.Interface))
                await SendAsync(socket, BroadcastCodec.Write(BroadcastKind.Announcement, options.Self), remote, remote.ToString(), cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask SendToEveryInterfaceAsync(BroadcastKind kind, CancellationToken cancellationToken)
    {
        byte[] datagram = BroadcastCodec.Write(kind, options.Self);
        foreach (LocalInterface local in LocalInterfaces.Read(DiscoverySource.Broadcast, options.Time, options.Facts))
        {
            if (kind == BroadcastKind.Announcement && !gate.TryPass(Now, local.Index))
                continue;

            foreach (Socket socket in sockets)
            {
                foreach (IPEndPoint target in Targets(socket.AddressFamily, local))
                    await SendAsync(socket, datagram, target, local.Name, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private IEnumerable<IPEndPoint> Targets(AddressFamily family, LocalInterface local)
    {
        if (family == AddressFamily.InterNetwork)
        {
            foreach ((IPAddress address, IPAddress mask) in local.IPv4)
            {
                if (LocalInterface.DirectedBroadcast(address, mask) is { } broadcast)
                    yield return new IPEndPoint(broadcast, options.Port);
            }
        }
        else if (local.IPv6.Count > 0)
        {
            IPAddress allNodes = IPAddress.Parse(AllNodes);
            allNodes.ScopeId = local.Index;
            yield return new IPEndPoint(allNodes, options.Port);
        }
    }

    private async ValueTask SendAsync(Socket socket, byte[] datagram, IPEndPoint target, string subject, CancellationToken cancellationToken)
    {
        try
        {
            await socket.SendToAsync(datagram, SocketFlags.None, target, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (cancellationToken.IsCancellationRequested && e is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Stopping: the socket closed under the send.
        }
        catch (SocketException e)
        {
            Record(DiscoveryFactKind.InterfaceSkipped, subject, $"{target}: {e.SocketErrorCode}");
        }
    }

    private void Record(DiscoveryFactKind kind, string subject, string detail) =>
        options.Facts.Record(new DiscoveryFact { At = options.Time.GetUtcNow(), Mechanism = DiscoverySource.Broadcast, Kind = kind, Subject = subject, Detail = detail });
}
