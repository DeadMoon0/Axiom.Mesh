using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Channels;
using Axiom.Mesh.Discovery;

namespace Axiom.Mesh.Testing;

/// <summary>
/// The LAN of an in-memory mesh: which node listens at which address, the discovery board every node's
/// <see cref="FakeDiscovery"/> announces on and hears from, the open connections, and a partition that splits it in
/// two. Connections are real in-memory streams into the target node's server, so admission, TLS and the protocol run
/// as on sockets. Thread-safe.
/// </summary>
public sealed class InMemoryNetwork
{
    private const int ObserverQueue = 256;

    private readonly object gate = new();
    private readonly Dictionary<IPEndPoint, Func<IPEndPoint, Stream>> listeners = [];
    private readonly List<InMemoryLink> links = [];
    private readonly Dictionary<DeviceId, Announcement> board = [];
    private readonly List<Observer> observers = [];
    private readonly HashSet<(IPAddress, IPAddress)> severed = [];
    private readonly Dictionary<string, IPAddress> names = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<IPAddress>? isolated;
    private bool held;

    /// <summary>Whether two addresses can reach each other now: always, unless a partition puts them on different sides.</summary>
    /// <param name="a">One address.</param>
    /// <param name="b">The other.</param>
    /// <returns><see langword="true"/> when they are on the same side.</returns>
    public bool CanReach(IPAddress a, IPAddress b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        lock (gate)
            return Reachable(a, b);
    }

    /// <summary>
    /// Holds every announcement on the board undelivered until <see cref="Release"/>: a mesh starts its nodes and seeds
    /// their trust first, so that no node dials a peer that does not know it yet.
    /// </summary>
    public void Hold()
    {
        lock (gate)
            held = true;
    }

    /// <summary>Delivers what the board holds to every observer, and every later announcement as it comes.</summary>
    public void Release()
    {
        lock (gate)
        {
            held = false;
            foreach (Observer observer in observers)
                Replay(observer);
        }
    }

    /// <summary>Gives an address a host name, as a LAN's DNS would: a pairing code may name a host instead of an address.</summary>
    /// <param name="host">The name.</param>
    /// <param name="address">The address it stands for.</param>
    public void Name(string host, IPAddress address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(address);
        lock (gate)
            names[host] = address;
    }

    /// <summary>The addresses a host name stands for on this network: the one <see cref="Name"/> gave it, or none.</summary>
    /// <param name="host">The name.</param>
    /// <returns>The addresses.</returns>
    public IReadOnlyList<IPAddress> Resolve(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (gate)
            return names.TryGetValue(host, out IPAddress? address) ? [address] : [];
    }

    /// <summary>Makes a node reachable at an endpoint.</summary>
    /// <param name="endpoint">The node's mesh endpoint.</param>
    /// <param name="connect">Opens a connection to the node, given the address it sees as the caller's.</param>
    public void Listen(IPEndPoint endpoint, Func<IPEndPoint, Stream> connect)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(connect);
        lock (gate)
            listeners[endpoint] = connect;
    }

    /// <summary>Makes the endpoint unreachable, as a stopped node is; open connections end with the node's server.</summary>
    /// <param name="endpoint">The node's mesh endpoint.</param>
    public void Unlisten(IPEndPoint endpoint)
    {
        lock (gate)
            listeners.Remove(endpoint);
    }

    /// <summary>Opens a connection from one address to a listening endpoint, as a TCP connect would.</summary>
    /// <param name="from">The caller's address and port.</param>
    /// <param name="to">The target endpoint.</param>
    /// <returns>The caller's end of the connection.</returns>
    /// <exception cref="IOException">Nothing listens there, or a partition separates the two.</exception>
    public Stream Connect(IPEndPoint from, IPEndPoint to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        Func<IPEndPoint, Stream>? connect;
        lock (gate)
        {
            if (!Reachable(from.Address, to.Address))
                throw new IOException($"{to} cannot be reached from {from.Address}: the network is partitioned.");
            if (!listeners.TryGetValue(to, out connect))
                throw new IOException($"Nothing listens at {to}: connection refused.");
        }

        Stream end;
        try
        {
            end = connect(from);
        }
        catch (InvalidOperationException ex)
        {
            throw new IOException($"The node at {to} is not running: connection refused.", ex);
        }

        var link = new InMemoryLink(end, from, to);
        lock (gate)
        {
            links.RemoveAll(l => l.IsClosed);
            links.Add(link);
        }

        return link;
    }

    /// <summary>
    /// Splits the network in two: the given addresses on one side, every other on the other. Connections across are cut,
    /// dials across fail, and announcements are heard on the announcer's side only. A second partition replaces the first.
    /// </summary>
    /// <param name="side">The addresses of one side.</param>
    public void Partition(IEnumerable<IPAddress> side)
    {
        ArgumentNullException.ThrowIfNull(side);
        InMemoryLink[] crossing;
        lock (gate)
        {
            isolated = [.. side];
            crossing = [.. links.Where(l => !Reachable(l.From.Address, l.To.Address))];
        }

        foreach (InMemoryLink link in crossing)
            link.Cut();
    }

    /// <summary>
    /// Cuts the link between two addresses alone, both ways: their connections end, their dials fail and they no longer
    /// hear each other, while each still reaches every other address. Shapes a mesh that is not fully connected, such as
    /// a relay through a third node.
    /// </summary>
    /// <param name="a">One address.</param>
    /// <param name="b">The other.</param>
    public void Sever(IPAddress a, IPAddress b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        InMemoryLink[] crossing;
        lock (gate)
        {
            severed.Add((a, b));
            severed.Add((b, a));
            crossing = [.. links.Where(l => !Reachable(l.From.Address, l.To.Address))];
        }

        foreach (InMemoryLink link in crossing)
            link.Cut();
    }

    /// <summary>Joins the two sides again and mends every severed link; every node hears again what it could not.</summary>
    public void Heal()
    {
        lock (gate)
        {
            isolated = null;
            severed.Clear();
            foreach (Observer observer in observers)
                Replay(observer);
        }
    }

    /// <summary>Puts a node's announcement on the board and delivers it to every node on its side but itself.</summary>
    /// <param name="announcement">The node's claim.</param>
    internal void Announce(Announcement announcement)
    {
        lock (gate)
        {
            board[announcement.Device] = announcement;
            if (held)
                return;
            foreach (Observer observer in observers)
            {
                if (observer.Self != announcement.Device && Reachable(observer.Address, announcement.Address.Address))
                    observer.Queue.Writer.TryWrite(announcement);
            }
        }
    }

    /// <summary>Takes a node's announcement off the board: nobody who starts observing later hears it.</summary>
    /// <param name="device">The node's device.</param>
    internal void Withdraw(DeviceId device)
    {
        lock (gate)
            board.Remove(device);
    }

    /// <summary>Starts delivering to a node's observer: first what the board holds on its side, then what is announced.</summary>
    /// <param name="self">The observing node's own announcement.</param>
    /// <returns>The observer's queue; pass it to <see cref="Leave"/> when done.</returns>
    internal Channel<Announcement> Join(Announcement self)
    {
        var observer = new Observer(self.Device, self.Address.Address, Channel.CreateBounded<Announcement>(new BoundedChannelOptions(ObserverQueue)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        }));
        lock (gate)
        {
            observers.Add(observer);
            Replay(observer);
        }

        return observer.Queue;
    }

    /// <summary>Stops delivering to an observer.</summary>
    /// <param name="queue">The queue <see cref="Join"/> returned.</param>
    internal void Leave(Channel<Announcement> queue)
    {
        lock (gate)
            observers.RemoveAll(o => ReferenceEquals(o.Queue, queue));
        queue.Writer.TryComplete();
    }

    private void Replay(Observer observer)
    {
        if (held)
            return;
        foreach (Announcement claim in board.Values)
        {
            if (claim.Device != observer.Self && Reachable(observer.Address, claim.Address.Address))
                observer.Queue.Writer.TryWrite(claim);
        }
    }

    private bool Reachable(IPAddress a, IPAddress b) =>
        (isolated is null || isolated.Contains(a) == isolated.Contains(b)) && !severed.Contains((a, b));

    private sealed record Observer(DeviceId Self, IPAddress Address, Channel<Announcement> Queue);
}
