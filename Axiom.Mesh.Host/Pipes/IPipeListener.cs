using System;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Host;

/// <summary>
/// The pipe the daemon's clients connect to (LOCAL-SPEC.md §2.1, §2.2): the only platform code of the daemon. A listener
/// creates the pipe so that nobody else can stand in for it and nobody off the machine can reach it, and hands every
/// connection over with the process identity of its peer.
/// </summary>
internal interface IPipeListener : IAsyncDisposable
{
    /// <summary>Where clients connect: the pipe's name or the socket's path, as printed at start.</summary>
    string Location { get; }

    /// <summary>The operating-system user the daemon runs as, named as the listener names its peers' users.</summary>
    string HostUser { get; }

    /// <summary>Whether the daemon runs as a system service (LOCAL-SPEC.md §2.2: the allow list applies then).</summary>
    bool RunningAsService { get; }

    /// <summary>Waits for the next connection whose peer's process identity is read.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The connection.</returns>
    ValueTask<PipeConnection> AcceptAsync(CancellationToken cancellationToken);
}
