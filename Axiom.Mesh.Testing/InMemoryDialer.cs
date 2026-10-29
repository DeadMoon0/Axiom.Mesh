using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Testing;

/// <summary>
/// Dials on an <see cref="InMemoryNetwork"/>: connects to the target node's server in memory, from this node's address
/// and a fresh port, so both ends run the full path (admission, mutual TLS, AMP/1) as over TCP. A target nobody listens
/// at, or one across a partition, fails like a refused connect.
/// </summary>
public sealed class InMemoryDialer : IDialer
{
    private readonly InMemoryNetwork network;
    private readonly IPAddress address;
    private int port = 49151;

    /// <summary>The dialler of the node at <paramref name="address"/>.</summary>
    /// <param name="network">The network.</param>
    /// <param name="address">The dialling node's address.</param>
    public InMemoryDialer(InMemoryNetwork network, IPAddress address)
    {
        this.network = network ?? throw new ArgumentNullException(nameof(network));
        this.address = address ?? throw new ArgumentNullException(nameof(address));
    }

    /// <inheritdoc />
    public ValueTask<Stream> DialAsync(IPEndPoint remote, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remote);
        cancellationToken.ThrowIfCancellationRequested();
        var from = new IPEndPoint(address, Interlocked.Increment(ref port));
        return ValueTask.FromResult(network.Connect(from, remote));
    }
}
