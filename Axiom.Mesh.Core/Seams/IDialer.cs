using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>
/// Opens the connection to a peer the node decided to dial (SPEC.md §4.1, §4.4): the one seam a dialled byte stream
/// enters by. A real node dials TCP (<see cref="TcpDialer"/>); an in-memory mesh connects to the other node's server
/// without a socket. Admission was decided before the call; TLS, the protocol and the drain run on the stream after it.
/// </summary>
public interface IDialer
{
    /// <summary>Connects to a peer.</summary>
    /// <param name="remote">The peer's mesh listener.</param>
    /// <param name="cancellationToken">Cancels connecting; a dialler gives up after 10 seconds on its own.</param>
    /// <returns>The connected stream; the node owns and closes it.</returns>
    /// <exception cref="IOException">The peer could not be reached (a <see cref="System.Net.Sockets.SocketException"/> included).</exception>
    ValueTask<Stream> DialAsync(IPEndPoint remote, CancellationToken cancellationToken);
}
