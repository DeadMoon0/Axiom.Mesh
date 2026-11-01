using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE.Adoption;
using Axiom.LWSE.Errors;
using Axiom.Mesh.Local;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Core;

/// <summary>
/// The node's local port (LOCAL-SPEC.md, DESIGN.md §10.2–§10.4): where every program on the device and every in-process
/// service talks to the node over <c>axiom-mesh-local/1</c>. The Host hands it each pipe connection with the process
/// identity it read from the operating system; an in-process service connects over an in-memory pair. Both are adopted
/// into the node's one server, so admission, observers and the drain cover them like any connection.
/// </summary>
/// <remarks>
/// <para>
/// What the port enforces: the <c>Hello</c> checks of §4.2 (an in-process name only on its own pair, a spawned or
/// external name only with its token, compared in constant time; one connection per service name; 64 user
/// connections; 16 waiting for <c>Hello</c>); the dispatcher's checks of §3.4 and §6 with <c>UnknownRequest</c> for every
/// request this Host does not serve yet; every user-key request logged with its process id and executable, and
/// confinable to listed executables (§2.2); subscriptions bounded per subscription, per connection and per Host (§7.4);
/// operations (§8); liveness (§4.4); the 30-second frame bound (§3.1); and <c>Drain</c> when the node stops (§4.5).
/// </para>
/// <para>
/// Whether an operating-system user may connect at all is the Host's decision, made before it calls
/// <see cref="AdoptAsync"/> (§2.2).
/// </para>
/// </remarks>
public sealed class LocalPort
{
    private static readonly TimeSpan Reservation = TimeSpan.FromSeconds(Limits.LocalHelloSeconds);

    private readonly ConcurrentDictionary<IPEndPoint, LocalArrival> arrivals = new();
    private readonly ConcurrentDictionary<LocalConnection, Task> open = new();
    private readonly ConcurrentDictionary<LocalConnection, TaskCompletionSource> endings = new();
    private IConnectionAdoption? adoption;
    private MeshNode? node;
    private long hostBytes;
    private int lastAddress;
    private int draining;

    internal LocalPort(LocalPortOptions options, NodeFacts facts, TimeProvider time, ILogger log, ChangeBus bus)
    {
        Options = options;
        Facts = facts;
        Time = time;
        Log = log;
        Handler = new LocalPortHandler(this);
        Hub = new LocalEventHub(this, bus);
        Services = new ServiceDirectory(options.Services);
    }

    /// <summary>Connections served now, waiting for <c>Hello</c> or past it.</summary>
    public int Connections => open.Count;

    /// <summary>What the configuration told the port.</summary>
    internal LocalPortOptions Options { get; }

    /// <summary>The node's facts: connections admitted, refused and ended.</summary>
    internal NodeFacts Facts { get; }

    /// <summary>The node's clock.</summary>
    internal TimeProvider Time { get; }

    /// <summary>Where user-key requests and handler failures are logged.</summary>
    internal ILogger Log { get; }

    /// <summary>The protocol handler the node's module registers.</summary>
    internal LocalPortHandler Handler { get; }

    /// <summary>The event hub: change sets and peer views into events.</summary>
    internal LocalEventHub Hub { get; }

    /// <summary>The connections past and waiting for <c>Hello</c>.</summary>
    internal ConnectionBook Book { get; } = new();

    /// <summary>The configured services and the spawned ones' tokens as they stand now (LOCAL-SPEC.md §4.2).</summary>
    internal ServiceDirectory Services { get; }

    /// <summary>Which service holds which Package kind (LOCAL-SPEC.md §9.2).</summary>
    internal KindRegistry Kinds { get; } = new();

    /// <summary>The dispatcher's table; a test or a later milestone replaces it.</summary>
    internal RequestRoutes Routes { get; set; } = RequestRoutes.Default;

    /// <summary>How executable paths compare on this platform, for the confinement of user-key requests.</summary>
    internal StringComparison PathComparison { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The node, once it started.</summary>
    internal MeshNode Node => node ?? throw new InvalidOperationException("The local port serves only once its node started.");

    /// <summary>
    /// Serves a pipe connection the Host accepted: adopted into the node's local port with the process identity the Host
    /// read for it (LOCAL-SPEC.md §2.2). The port owns the stream from now on.
    /// </summary>
    /// <param name="stream">The connection.</param>
    /// <param name="identity">Who is at the other end.</param>
    /// <param name="cancellationToken">A cancelled token refuses the connection.</param>
    /// <returns>A task that completes when the connection ended; at once when it was refused (the node stops, or admission refused it).</returns>
    public Task AdoptAsync(Stream stream, ProcessIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.Kind == ProcessKind.Unknown)
            throw new ArgumentException("A connection is adopted with the identity of a program on the pipe or of an in-process service.", nameof(identity));
        if (cancellationToken.IsCancellationRequested || Volatile.Read(ref draining) != 0 || adoption is not { } seam)
            return RefuseAsync(stream);

        IPEndPoint remote = NextRemote();
        var activity = new LinkActivity(Time, tracksFrames: true);
        arrivals[remote] = new LocalArrival(identity, activity);
        AdmissionLease? lease;
        try
        {
            if (!seam.TryReserve(Ports.Local, remote, Reservation, out lease))
                return Forget(remote, stream);
        }
        catch (Exception ex) when (ex is AdoptionRefusedException or InvalidOperationException)
        {
            return Forget(remote, stream);
        }

        try
        {
            return Quietly(seam.AdoptAsync(lease, new ActivityStream(stream, activity), new IPEndPoint(IPAddress.Loopback, 0)));
        }
        catch (Exception ex) when (ex is AdoptionRefusedException or InvalidOperationException)
        {
            lease.Dispose();
            return Forget(remote, stream);
        }
    }

    /// <summary>
    /// Sets the token of a spawned service for the child the Host is about to start (LOCAL-SPEC.md §4.2, decision 27): the
    /// child's connections present it, one at a time, until <see cref="ClearSpawnedToken"/>. Call it before the child starts,
    /// with a fresh token of 32 random bytes per start.
    /// </summary>
    /// <param name="serviceName">The service's name, as the configuration lists it as spawned.</param>
    /// <param name="token">The child's token; never <see cref="ServiceToken.None"/>.</param>
    /// <exception cref="ArgumentException">The name is not a configured spawned service, or the token is none.</exception>
    public void SetSpawnedToken(string serviceName, ServiceToken token) => Services.Set(serviceName, token);

    /// <summary>
    /// Withdraws a spawned service's token once its child exited (LOCAL-SPEC.md §4.2): from now on no connection proves the
    /// name, all-zero tokens included, until the next child's token is set. A connection already admitted keeps running.
    /// </summary>
    /// <param name="serviceName">The service's name, as the configuration lists it as spawned.</param>
    /// <exception cref="ArgumentException">The name is not a configured spawned service.</exception>
    public void ClearSpawnedToken(string serviceName) => Services.Clear(serviceName);

    /// <summary>
    /// Connects an in-process service (LOCAL-SPEC.md §4.2, DESIGN.md §10.3): an in-memory pair whose other end the port
    /// adopts as that service's, the only connection on which its name is granted. Say <c>Hello</c> with role service,
    /// the name and an all-zero token.
    /// </summary>
    /// <param name="serviceName">The service's name, as the configuration lists it in process.</param>
    /// <returns>The service's end of the connection; give it to <c>LocalClient.OpenAsync</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="serviceName"/> is empty.</exception>
    public Stream ConnectInProcess(string serviceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceName);
        (Stream service, Stream port) = MemoryDuplexStream.CreatePair();
        _ = AdoptAsync(port, ProcessIdentity.InProcess(serviceName), CancellationToken.None);
        return service;
    }

    /// <inheritdoc />
    public override string ToString() => $"local port, {open.Count} connections";

    /// <summary>Hands the port the server's adoption seam; called once, while the server is built.</summary>
    /// <param name="seam">The seam.</param>
    internal void Use(IConnectionAdoption seam) => adoption = seam;

    /// <summary>Hands the port its node, once started; requests are served from then on.</summary>
    /// <param name="running">The node.</param>
    internal void Attach(MeshNode running) => node = running;

    /// <summary>Takes what <see cref="AdoptAsync"/> recorded for a connection LWSE now serves.</summary>
    /// <param name="remote">The connection's remote endpoint, the port's synthetic one.</param>
    /// <param name="arrival">The identity and activity.</param>
    /// <returns><see langword="false"/> for a connection the port did not adopt.</returns>
    internal bool TryTakeArrival(IPEndPoint remote, [NotNullWhen(true)] out LocalArrival? arrival) =>
        arrivals.TryRemove(remote, out arrival);

    /// <summary>A connection began to be served.</summary>
    /// <param name="connection">The connection.</param>
    internal void Opened(LocalConnection connection)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        endings[connection] = ended;
        open[connection] = ended.Task;
    }

    /// <summary>A connection ended.</summary>
    /// <param name="connection">The connection.</param>
    internal void Closed(LocalConnection connection)
    {
        open.TryRemove(connection, out _);
        if (endings.TryRemove(connection, out TaskCompletionSource? ended))
            ended.TrySetResult();
    }

    /// <summary>
    /// A connection's outbox holds more or fewer bytes. Past 256 MiB across every connection, the one holding the most is
    /// closed with <c>Bye(Unresponsive)</c> (LOCAL-SPEC.md §7.4).
    /// </summary>
    /// <param name="delta">The change.</param>
    internal void Account(long delta)
    {
        long total = Interlocked.Add(ref hostBytes, delta);
        if (delta <= 0 || total <= Limits.MaxUnacceptedBytesPerHost)
            return;
        LocalConnection? heaviest = open.Keys.MaxBy(c => c.Outbox.HeldBytes);
        heaviest?.CloseUnresponsive("the Host held more than 256 MiB for its clients, and this connection the most (LOCAL-SPEC.md §7.4)");
    }

    /// <summary>The <c>HelloAck</c> of an admitted connection (LOCAL-SPEC.md §4.2).</summary>
    /// <param name="connection">The connection.</param>
    /// <returns>The message.</returns>
    internal HelloAck AckFor(LocalConnection connection)
    {
        MeshNode running = Node;
        StoreWriter writer = running.Writer;
        bool otherAccount = connection.Origin.Kind == ProcessKind.Pipe && Options.HostUser.Length != 0
            && !string.Equals(connection.Origin.User, Options.HostUser, StringComparison.OrdinalIgnoreCase);
        return new HelloAck
        {
            HostVersion = Options.HostVersion,
            Role = connection.Role,
            Capabilities = otherAccount ? HostCapabilities.OtherAccount
                : running.Files.StorePath is not null ? HostCapabilities.StoreReadable
                : HostCapabilities.None,
            Identity = LocalRecords.IdentityOf(writer.Trust, running.UserKeyHeld),
            StoreEpoch = writer.Epoch,
            StoreSeq = writer.Totals.StoreSeq,
        };
    }

    /// <summary>
    /// Drains every local connection (LOCAL-SPEC.md §4.5): <c>Drain</c> with the configured grace, new requests answered
    /// <c>Draining</c>, operations cancelled; when every client left or the grace elapsed, <c>Bye(Shutdown)</c> to the rest.
    /// Connections still waiting for <c>Hello</c> are closed without a frame, and nothing is adopted from now on.
    /// </summary>
    /// <param name="cancellationToken">Cuts the grace short.</param>
    /// <returns>A task that completes when every connection was told.</returns>
    internal async Task DrainAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref draining, 1) != 0)
            return;
        TimeSpan grace = Clamp(Options.DrainGrace);
        LocalConnection[] all = [.. open.Keys];
        foreach (LocalConnection connection in all)
        {
            if (connection.Role == Role.None)
                connection.CloseWithoutFrame("the Host stops (LOCAL-SPEC.md §4.5)");
            else
                connection.BeginDrain(grace);
        }

        Task[] ended = [.. open.Values];
        if (ended.Length != 0)
        {
            try
            {
                await Task.WhenAny(Task.WhenAll(ended), Task.Delay(grace, Time, cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cut short: Bye(Shutdown) now.
            }
        }

        foreach (LocalConnection connection in open.Keys)
            connection.Shutdown();
    }

    /// <summary>Whether the port drains: a <c>Hello</c> now is answered <c>Bye(Shutdown)</c>.</summary>
    internal bool IsDraining => Volatile.Read(ref draining) != 0;

    private static TimeSpan Clamp(TimeSpan grace) =>
        grace < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1)
        : grace > TimeSpan.FromMilliseconds(Limits.DrainGraceMaxMilliseconds) ? TimeSpan.FromMilliseconds(Limits.DrainGraceMaxMilliseconds)
        : grace;

    // Admission counts per client address (an IPv4 address or an IPv6 /64), so every local connection gets an address of
    // its own in 127.0.0.0/8: 16 million at once, far beyond the 64 + services §12 allows.
    private IPEndPoint NextRemote()
    {
        int n = (int)((uint)Interlocked.Increment(ref lastAddress) % 0xFF_FFFE) + 1;
        return new IPEndPoint(new IPAddress([127, (byte)(n >> 16), (byte)(n >> 8), (byte)n]), 1);
    }

    private Task Forget(IPEndPoint remote, Stream stream)
    {
        if (arrivals.TryRemove(remote, out LocalArrival? arrival))
            arrival.Activity.Dispose();
        return RefuseAsync(stream);
    }

    private static async Task RefuseAsync(Stream stream) => await stream.DisposeAsync().ConfigureAwait(false);

    private static async Task Quietly(Task served)
    {
        try
        {
            await served.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The server reported whatever ended the connection to its observers.
        }
    }
}
