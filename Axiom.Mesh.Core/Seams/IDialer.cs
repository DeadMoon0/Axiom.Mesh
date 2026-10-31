using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>
/// Opens the connection to a peer the node decided to dial (SPEC.md §4.1, §4.4): the one seam a dialled byte stream
/// enters by. A real node dials TCP (<see cref="TcpDialer"/>); an in-memory mesh connects to the other node's server
/// without a socket. Admission was decided before the call; TLS, the protocol and the drain run on the stream after it.
/// A host name (a pairing code may carry one, PAIR-SPEC.md §2.2) is resolved by the same seam first, so admission still
/// decides on an address.
/// </summary>
public interface IDialer
{
    /// <summary>Connects to a peer.</summary>
    /// <param name="remote">The peer's mesh listener.</param>
    /// <param name="cancellationToken">Cancels connecting; a dialler gives up after 10 seconds on its own.</param>
    /// <returns>The connected stream; the node owns and closes it.</returns>
    /// <exception cref="IOException">The peer could not be reached (a <see cref="System.Net.Sockets.SocketException"/> included).</exception>
    ValueTask<Stream> DialAsync(IPEndPoint remote, CancellationToken cancellationToken);

    /// <summary>The addresses a host name stands for, in the order to try them.</summary>
    /// <param name="host">A DNS name; never an IP literal (the caller parses those itself).</param>
    /// <param name="cancellationToken">Cancels resolving.</param>
    /// <returns>The addresses; empty when the name stands for none.</returns>
    ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}
