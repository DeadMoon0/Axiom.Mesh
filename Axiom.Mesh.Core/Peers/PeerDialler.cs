using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE.Adoption;
using Axiom.LWSE.Errors;

namespace Axiom.Mesh.Core;

/// <summary>
/// Runs the dials the peer table decides on (SPEC.md §4.4, §4.6): admission reserves a place on the outbound port
/// before the connection is opened, the <see cref="IDialer"/> opens it, and the server adopts it, so TLS as client,
/// AMP/1, the observers and the drain run on it as on an accepted one. Each dial runs on its own and reports how it
/// ended, which decides the backoff.
/// </summary>
internal sealed class PeerDialler
{
    private static readonly TimeSpan Reservation = TimeSpan.FromSeconds(Limits.TlsHandshakeSeconds);

    private readonly PeerTable table;
    private readonly IDialer dialer;
    private readonly NodeFacts facts;
    private readonly object running = new();
    private readonly HashSet<Task> dials = [];
    private IConnectionAdoption? adoption;

    /// <summary>A dialler for a node.</summary>
    /// <param name="table">Where the dials come from and their outcomes go.</param>
    /// <param name="dialer">What opens the connections.</param>
    /// <param name="facts">Where to record what happens.</param>
    public PeerDialler(PeerTable table, IDialer dialer, NodeFacts facts)
    {
        this.table = table;
        this.dialer = dialer;
        this.facts = facts;
    }

    /// <summary>Hands the dialler the server's adoption seam; called once, while the server is built.</summary>
    /// <param name="seam">The seam.</param>
    public void Use(IConnectionAdoption seam) => adoption = seam;

    /// <summary>Runs dials until stopped, then waits for the connections they opened to end.</summary>
    /// <param name="cancellationToken">Stops taking dials.</param>
    /// <returns>A task that completes when stopped.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (DialRequest request in table.Requests.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                Track(DialAsync(request, cancellationToken));
        }
        finally
        {
            Task[] pending;
            lock (running)
                pending = [.. dials];
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Dials an address of a pairing code once (PAIR-SPEC.md §4.1), outside the peer table: no backoff, no retry. The
    /// pairing window decides that the handshake offers the pairing ALPN and pins the code's key.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="cancellationToken">Stops the dial; a connection once open runs until it ends.</param>
    /// <returns>A task that completes when the dial failed or the connection ended.</returns>
    public async Task DialPairingAsync(IPEndPoint address, CancellationToken cancellationToken)
    {
        IConnectionAdoption seam = adoption ?? throw new InvalidOperationException("The dialler runs before the server was built.");
        AdmissionLease? lease;
        try
        {
            if (!seam.TryReserve(Ports.Outbound, address, Reservation, out lease))
            {
                facts.Record(NodeFactKind.DialFailed, 0, address, default, "pairing: admission refused the place");
                return;
            }
        }
        catch (AdoptionRefusedException ex)
        {
            facts.Record(NodeFactKind.DialFailed, 0, address, default, "pairing: " + ex.Message);
            return;
        }

        facts.Record(NodeFactKind.Dialling, 0, address, default, "pairing");
        Stream stream;
        try
        {
            stream = await dialer.DialAsync(address, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or OperationCanceledException)
        {
            lease.Dispose();
            facts.Record(NodeFactKind.DialFailed, 0, address, default, "pairing: " + ex.Message);
            return;
        }

        try
        {
            await seam.AdoptAsync(lease, stream, LocalOf(stream)).ConfigureAwait(false);
        }
        catch (AdoptionRefusedException)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The server reported whatever ended the connection to its observers; the pairing shell saw the rest.
        }
    }

    /// <summary>The addresses a host name of a pairing code stands for (PAIR-SPEC.md §2.2), through the node's dialler.</summary>
    /// <param name="host">The name.</param>
    /// <param name="cancellationToken">Cancels resolving.</param>
    /// <returns>The addresses, in the order to try them; empty when it stands for none or cannot be resolved now.</returns>
    public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return await dialer.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ArgumentException)
        {
            facts.Record(NodeFactKind.DialFailed, 0, null, default, $"pairing: {host} did not resolve: {ex.Message}");
            return [];
        }
    }

    private void Track(Task dial)
    {
        lock (running)
            dials.Add(dial);
        _ = dial.ContinueWith(
            done =>
            {
                lock (running)
                    dials.Remove(done);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task DialAsync(DialRequest request, CancellationToken cancellationToken)
    {
        IConnectionAdoption seam = adoption ?? throw new InvalidOperationException("The dialler runs before the server was built.");
        AdmissionLease? lease;
        try
        {
            if (!seam.TryReserve(Ports.Outbound, request.Address, Reservation, out lease))
            {
                table.DialEnded(request, DialOutcome.Skipped, "admission refused the place");
                return;
            }
        }
        catch (AdoptionRefusedException ex)
        {
            table.DialEnded(request, DialOutcome.Skipped, ex.Message);
            return;
        }

        facts.Record(NodeFactKind.Dialling, 0, request.Address, request.Device ?? default, request.Device is null ? "static peer" : string.Empty);
        Stream stream;
        try
        {
            stream = await dialer.DialAsync(request.Address, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lease.Dispose();
            table.DialEnded(request, DialOutcome.Skipped, "the node stops");
            return;
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or OperationCanceledException)
        {
            lease.Dispose();
            facts.Record(NodeFactKind.DialFailed, 0, request.Address, request.Device ?? default, ex.Message);
            table.DialEnded(request, DialOutcome.Failed, ex.Message);
            return;
        }

        Task served;
        try
        {
            served = seam.AdoptAsync(lease, stream, LocalOf(stream));
        }
        catch (AdoptionRefusedException ex)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            table.DialEnded(request, DialOutcome.Skipped, ex.Message);
            return;
        }

        try
        {
            await served.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The server reported whatever ended the connection to its observers; what counts here is whether it established.
        }

        table.DialEnded(request, DialOutcome.Ended, "the connection ended before the peer's Hello");
    }

    private static IPEndPoint LocalOf(Stream stream) =>
        stream is NetworkStream { Socket.LocalEndPoint: IPEndPoint local } ? local : new IPEndPoint(IPAddress.IPv6Any, 0);
}
