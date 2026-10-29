using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// Discovery by DNS-SD over mDNS (SPEC.md §3.1): UDP 5353, the groups 224.0.0.251 and ff02::fb joined on every
/// interface, one socket per address family. Answers queries for the service type and announces itself periodically,
/// every answer and announcement through the <see cref="AnnounceGate"/>. Binds with ReuseAddress so that it coexists
/// with the operating system's own responder (Bonjour, Avahi, the Windows DNS client).
/// </summary>
/// <remarks>
/// A thin, dishonest shell over <see cref="DnsSdCodec"/>. Multicast is filtered on hosted CI runners, so this shell is
/// verified by hand (STRUCTURAL-DEBT.md entry 2); the codec under it is tested in full.
/// </remarks>
public sealed class MdnsDiscovery : IDiscovery, IAsyncDisposable
{
    private readonly MdnsDiscoveryOptions options;
    private readonly List<Socket> sockets = [];
    private readonly List<Task> loops = [];
    private readonly Subscribers subscribers = new();
    private readonly AnnounceGate gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly HashSet<(AddressFamily Family, int Index)> joined = [];
    private readonly IPAddress group4 = IPAddress.Parse(DnsSdCodec.IPv4Group);
    private readonly long started;
    private volatile IReadOnlyList<LocalInterface> interfaces = [];

    private MdnsDiscovery(MdnsDiscoveryOptions options)
    {
        this.options = options;
        started = options.Time.GetTimestamp();
    }

    /// <summary>Binds the sockets, joins the groups and starts receiving (and, unless the interval is infinite, announcing).</summary>
    /// <param name="options">How to run.</param>
    /// <returns>The running discovery; dispose it to stop.</returns>
    public static MdnsDiscovery Open(MdnsDiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Intervals.Check(options.AnnounceInterval, nameof(options.AnnounceInterval));
        var discovery = new MdnsDiscovery(options);
        discovery.Start();
        return discovery;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Announcement> ObserveAsync(CancellationToken cancellationToken) => subscribers.ObserveAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask AnnounceAsync(CancellationToken cancellationToken)
    {
        foreach (LocalInterface local in Refresh())
            await AnswerOnAsync(local, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Multicasts a query for the service type on every interface. Queries are not gated.</summary>
    /// <param name="cancellationToken">Stops sending.</param>
    /// <returns>A task that completes when every interface was tried.</returns>
    public async ValueTask QueryAsync(CancellationToken cancellationToken)
    {
        byte[] query = DnsSdCodec.WriteQuery();
        foreach (LocalInterface local in Refresh())
            await SendOnAsync(local, query, cancellationToken).ConfigureAwait(false);
    }

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
        Bind(IPAddress.Any);
        Bind(IPAddress.IPv6Any);
        Refresh();
        foreach (Socket socket in sockets)
            loops.Add(ReceiveAsync(socket, stopping.Token));
        if (options.AnnounceInterval != Timeout.InfiniteTimeSpan)
            loops.Add(AnnounceEveryAsync(options.AnnounceInterval, stopping.Token));
    }

    private void Bind(IPAddress address)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // Another responder on this host holds 5353 too; ReuseAddress lets both receive the group's packets.
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = false;
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.PacketInformation, true);
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastTimeToLive, 255);
            }
            else
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            }

            socket.MulticastLoopback = true;
            socket.Bind(new IPEndPoint(address, DnsSdCodec.Port));
            sockets.Add(socket);
        }
        catch (SocketException e)
        {
            socket.Dispose();
            Record(DiscoveryFactKind.SocketUnavailable, $"udp {address}:{DnsSdCodec.Port}", e.SocketErrorCode.ToString());
        }
    }

    // Reads the interfaces again and joins the groups on those that came up since; returns the current list.
    private IReadOnlyList<LocalInterface> Refresh()
    {
        IReadOnlyList<LocalInterface> current = LocalInterfaces.Read(DiscoverySource.Mdns, options.Time, options.Facts);
        lock (joined)
        {
            foreach (LocalInterface local in current)
            {
                foreach (Socket socket in sockets)
                    Join(socket, local);
            }
        }

        interfaces = current;
        return current;
    }

    private void Join(Socket socket, LocalInterface local)
    {
        bool v4 = socket.AddressFamily == AddressFamily.InterNetwork;
        if ((v4 ? local.IPv4.Count : local.IPv6.Count) == 0 || joined.Contains((socket.AddressFamily, local.Index)))
            return;

        try
        {
            if (v4)
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(group4, local.IPv4[0].Address));
            else
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(IPAddress.Parse(DnsSdCodec.IPv6Group), local.Index));
            joined.Add((socket.AddressFamily, local.Index));
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            Record(DiscoveryFactKind.InterfaceSkipped, local.Name, $"join {(v4 ? DnsSdCodec.IPv4Group : DnsSdCodec.IPv6Group)}: {e.Message}");
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
        // One byte more than the longest packet, so a longer one is seen as longer and refused.
        byte[] buffer = new byte[DnsSdCodec.MaxPacketLength + 1];
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
                continue;
            }
            catch (SocketException e)
            {
                Record(DiscoveryFactKind.ReceiveStopped, $"udp {socket.LocalEndPoint}", e.SocketErrorCode.ToString());
                return;
            }

            ReadOnlyMemory<byte> packet = buffer.AsMemory(0, received.ReceivedBytes);
            if (DnsSdCodec.IsServiceQuery(packet.Span))
            {
                if (Find(received.PacketInformation.Interface) is { } local)
                    await AnswerOnAsync(local, cancellationToken).ConfigureAwait(false);
                continue;
            }

            foreach (Announcement announcement in DnsSdCodec.ReadAnnouncements(packet.Span, ((IPEndPoint)received.RemoteEndPoint).Address))
            {
                if (announcement.Device != options.Self.Device)
                    subscribers.Publish(announcement);
            }
        }
    }

    private LocalInterface? Find(int index)
    {
        foreach (LocalInterface local in interfaces)
        {
            if (local.Index == index)
                return local;
        }

        return null;
    }

    private async ValueTask AnswerOnAsync(LocalInterface local, CancellationToken cancellationToken)
    {
        if (!gate.TryPass(Now, local.Index))
            return;

        var addresses = new List<IPAddress>();
        foreach ((IPAddress address, _) in local.IPv4)
            addresses.Add(address);
        addresses.AddRange(local.IPv6);
        if (addresses.Count > DnsSdCodec.MaxAddresses)
            addresses.RemoveRange(DnsSdCodec.MaxAddresses, addresses.Count - DnsSdCodec.MaxAddresses);

        await SendOnAsync(local, DnsSdCodec.WriteAnswer(options.Self, addresses), cancellationToken).ConfigureAwait(false);
    }

    // Choosing the outgoing interface and sending are two calls on a shared socket, so sends are serialised.
    private async ValueTask SendOnAsync(LocalInterface local, byte[] packet, CancellationToken cancellationToken)
    {
        try
        {
            await sending.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            foreach (Socket socket in sockets)
            {
                bool v4 = socket.AddressFamily == AddressFamily.InterNetwork;
                if ((v4 ? local.IPv4.Count : local.IPv6.Count) == 0)
                    continue;

                try
                {
                    IPEndPoint target;
                    if (v4)
                    {
                        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.IPv4[0].Address.GetAddressBytes());
                        target = new IPEndPoint(group4, DnsSdCodec.Port);
                    }
                    else
                    {
                        socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, local.Index);
                        IPAddress group6 = IPAddress.Parse(DnsSdCodec.IPv6Group);
                        group6.ScopeId = local.Index;
                        target = new IPEndPoint(group6, DnsSdCodec.Port);
                    }

                    await socket.SendToAsync(packet, SocketFlags.None, target, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (cancellationToken.IsCancellationRequested && e is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }
                catch (SocketException e)
                {
                    Record(DiscoveryFactKind.InterfaceSkipped, local.Name, $"send: {e.SocketErrorCode}");
                }
            }
        }
        finally
        {
            sending.Release();
        }
    }

    private void Record(DiscoveryFactKind kind, string subject, string detail) =>
        options.Facts.Record(new DiscoveryFact { At = options.Time.GetUtcNow(), Mechanism = DiscoverySource.Mdns, Kind = kind, Subject = subject, Detail = detail });
}
