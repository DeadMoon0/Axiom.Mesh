using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Local;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Host;

/// <summary>
/// The daemon's pipe in front of a node's local port (LOCAL-SPEC.md §2): created so that nobody else can stand in for it
/// and nobody off the machine reaches it, every connection admitted by its peer's process identity (§2.2) before a byte is
/// answered, and handed to <see cref="LocalPort.AdoptAsync"/>. The daemon runs one; an integration test runs one in front
/// of each real node it starts, so its clients and its command line connect exactly as they would to <c>axiom-meshd</c>.
/// </summary>
/// <remarks>
/// The pipe opens before the node starts, because the node is told whom it serves (<see cref="HostUser"/>), and because a
/// daemon whose pipe is taken must not touch the store another daemon holds.
/// </remarks>
public sealed class PipeHost : IAsyncDisposable
{
    private readonly IPipeListener listener;
    private readonly IReadOnlyList<string> allowUsers;
    private readonly ILogger log;
    private readonly CancellationTokenSource stopping = new();
    private Task accepting = Task.CompletedTask;
    private int served;
    private int disposed;

    private PipeHost(IPipeListener listener, IReadOnlyList<string> allowUsers, ILogger log)
    {
        this.listener = listener;
        this.allowUsers = allowUsers;
        this.log = log;
    }

    /// <summary>Where clients connect: the pipe's name or the socket's path.</summary>
    public string Location => listener.Location;

    /// <summary>The operating-system user the pipe admits, named as the platform names its peers' users.</summary>
    public string HostUser => listener.HostUser;

    /// <summary>Whether this process runs as a system service, so that <c>pipe.allowUsers</c> applies (LOCAL-SPEC.md §2.2).</summary>
    public bool RunningAsService => listener.RunningAsService;

    /// <summary>Creates the pipe; nothing is accepted until <see cref="Serve"/>.</summary>
    /// <param name="location">Where the pipe is (<see cref="PipeLocation.Resolve"/>).</param>
    /// <param name="allowUsers">Users a system service admits besides its own account; empty for a daemon run by a user.</param>
    /// <param name="time">The clock that bounds a client's first bytes.</param>
    /// <param name="log">Where refused connections are logged.</param>
    /// <returns>The open pipe.</returns>
    /// <exception cref="MeshException">The pipe is taken by another daemon, or this platform has none.</exception>
    public static PipeHost Open(PipeLocation location, IReadOnlyList<string> allowUsers, TimeProvider time, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(allowUsers);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        return new PipeHost(PipeListeners.Open(location, allowUsers, time), allowUsers, log);
    }

    /// <summary>Starts handing every admitted connection to <paramref name="node"/>'s local port, until disposed.</summary>
    /// <param name="node">The node; started with <see cref="HostUser"/> as its local port's host user.</param>
    /// <exception cref="InvalidOperationException">The pipe serves a node already.</exception>
    public void Serve(MeshNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (Interlocked.Exchange(ref served, 1) != 0)
            throw new InvalidOperationException("The pipe serves a node already; a pipe has one node behind it.");
        accepting = Task.Run(() => AcceptAsync(node, stopping.Token), CancellationToken.None);
    }

    /// <summary>Stops accepting and removes the pipe; connections already handed over end with the node.</summary>
    /// <returns>A task that completes when the pipe is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        await stopping.CancelAsync().ConfigureAwait(false);
        await listener.DisposeAsync().ConfigureAwait(false);
        await accepting.ConfigureAwait(false);
        stopping.Dispose();
    }

    // §2.2: the process identity decides before Hello is answered; a refused connection is closed without a frame.
    private async Task AcceptAsync(MeshNode node, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            PipeConnection connection;
            try
            {
                connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or ChannelClosedException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                log.LogWarning(ex, "The pipe failed to accept a connection; it goes on accepting.");
                continue;
            }

            PipeAdmissionVerdict verdict = PipeAdmission.Decide(connection.Identity, listener.HostUser, allowUsers, listener.RunningAsService);
            if (!verdict.IsAdmitted)
            {
                HostLog.PipeRefused(log, connection.Identity.Pid, connection.Identity.Executable, connection.Identity.User, verdict.Refusal!);
                await connection.Stream.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _ = node.Local.AdoptAsync(connection.Stream, connection.Identity, cancellationToken);
        }
    }
}
