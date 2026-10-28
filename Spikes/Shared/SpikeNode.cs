using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE;
using Axiom.LWSE.Admission;
using Axiom.LWSE.Server;

namespace Axiom.Mesh.Spikes;

/// <summary>
/// One mesh node as LWSE 0.2.0 allows it today: an inbound server that binds a socket, and a second, in-memory server
/// whose one port serves the connections this node dials. A dialled socket is pumped into the in-memory port, so
/// Core runs admission, the TLS layer (as client), the protocol, observers and drain on it like on an accepted one.
/// </summary>
public sealed class SpikeNode : IAsyncDisposable
{
    private const int OutboundPort = 1;

    private readonly LWSEServer _inbound;
    private readonly LWSEServer _outbound;

    private SpikeNode(string name, LWSEServer inbound, LWSEServer outbound)
    {
        Name = name;
        _inbound = inbound;
        _outbound = outbound;
    }

    public string Name { get; }

    public IPEndPoint Inbound => _inbound.Endpoints[0];

    public static async Task<SpikeNode> StartAsync(string name, int port, X509Certificate2 certificate, Func<ReadOnlyMemory<byte>, bool> isPinned, AdmissionOptions admission, SpikeLog log, CancellationToken cancellationToken)
    {
        TimeSpan drain = TimeSpan.FromSeconds(2);

        LWSEServer inbound = LWSEServer.Build(s => s
            .Admission(admission)
            .Drain(drain)
            .AddModule(new SpikeModule(name + "/in", log))
            .Listen(new IPEndPoint(IPAddress.Loopback, port), p => p
                .Through(SpikeModule.PinnedTls, new PinnedTlsOptions { Certificate = certificate, Role = TlsRole.Server, IsPinned = isPinned })
                .Speak(SpikeModule.Hello)));

        LWSEServer outbound = LWSEServer.Build(s => s
            .Admission(admission)
            .Drain(drain)
            .AddModule(new SpikeModule(name + "/out", log))
            .InMemory()
            .Listen(OutboundPort, p => p
                .Through(SpikeModule.PinnedTls, new PinnedTlsOptions { Certificate = certificate, Role = TlsRole.Client, IsPinned = isPinned })
                .Speak(SpikeModule.Hello)));

        await inbound.StartAsync(cancellationToken).ConfigureAwait(false);
        await outbound.StartAsync(cancellationToken).ConfigureAwait(false);
        return new SpikeNode(name, inbound, outbound);
    }

    /// <summary>Dials a peer and hands the socket to Core through the in-memory port.</summary>
    public async Task DialAsync(IPEndPoint peer, CancellationToken cancellationToken)
    {
        var socket = new Socket(peer.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(peer, cancellationToken).ConfigureAwait(false);
        var network = new NetworkStream(socket, ownsSocket: true);
        Stream adopted;
        try
        {
            adopted = _outbound.ConnectInMemory(OutboundPort, peer);
        }
        catch
        {
            await network.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _ = Pump.RunAsync(network, adopted);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _outbound.StopAsync(cancellationToken).ConfigureAwait(false);
        await _inbound.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _outbound.DisposeAsync().ConfigureAwait(false);
        await _inbound.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Copies both ways between two streams until either ends, then closes both. The cost of adoption without a Core API.</summary>
public static class Pump
{
    public static async Task RunAsync(Stream a, Stream b)
    {
        using var stop = new CancellationTokenSource();
        Task ab = CopyAsync(a, b, stop.Token);
        Task ba = CopyAsync(b, a, stop.Token);
        await Task.WhenAny(ab, ba).ConfigureAwait(false);
        await stop.CancelAsync().ConfigureAwait(false);
        await a.DisposeAsync().ConfigureAwait(false);
        await b.DisposeAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(ab, ba).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Either side ended first; the other side's read or write failing is the expected end.
        }
    }

    private static async Task CopyAsync(Stream from, Stream to, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            int read = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return;
            await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            await to.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
