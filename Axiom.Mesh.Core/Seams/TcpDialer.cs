using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>
/// Dials TCP as SPEC.md §4.1 asks: Nagle's algorithm off, keep-alive on, and a connect that gives up after 10 seconds
/// (<see cref="Limits.TlsHandshakeSeconds"/>, the same bound the handshake gets once connected).
/// </summary>
public sealed class TcpDialer : IDialer
{
    private readonly TimeProvider time;

    /// <summary>A dialler whose connect deadline runs on <paramref name="time"/>.</summary>
    /// <param name="time">The node's clock.</param>
    public TcpDialer(TimeProvider time) => this.time = time ?? throw new ArgumentNullException(nameof(time));

    /// <inheritdoc />
    /// <remarks>Through the system's resolver, IPv4 before IPv6; a name the resolver does not know stands for none.</remarks>
    public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            return [.. addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)];
        }
        catch (SocketException)
        {
            return [];
        }
    }

    /// <inheritdoc />
    public async ValueTask<Stream> DialAsync(IPEndPoint remote, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remote);
        var socket = new Socket(remote.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(Limits.TlsHandshakeSeconds), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
            try
            {
                await socket.ConnectAsync(remote, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new IOException($"Connecting to {remote} did not complete within {Limits.TlsHandshakeSeconds} seconds.");
            }

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
