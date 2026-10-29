using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE;
using Axiom.LWSE.Admission;
using Axiom.LWSE.Server;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// One node of a mesh: its store and index, its trust in rosters and grants, its connections to peers and the local
/// port its clients reach. Everything dishonest a node needs (files, database, keys, clock, sockets) is handed to it in
/// a <see cref="MeshNodeEnvironment"/>, so the same node runs on disk and sockets or entirely in memory.
/// </summary>
/// <remarks>
/// <para>
/// A node is one LWSE server with three ports (DESIGN.md §10.2): the mesh port, where peers connect through mutual
/// TLS pinned to the rosters; an adopt-only port for the connections the node dials; and an adopt-only port for local
/// clients. Admission allows four connections per peer address (SPEC.md §4.6).
/// </para>
/// <para>
/// Start it with <see cref="StartAsync"/>, stop it with <see cref="StopAsync"/>: every session says <c>Bye(Shutdown)</c>
/// before the node lets go of its store. Replication and the local port's requests follow in later versions.
/// </para>
/// </remarks>
public sealed class MeshNode : IAsyncDisposable
{
    private readonly MeshNodeOptions options;
    private readonly MeshNodeEnvironment environment;
    private readonly MeshDatabase database;
    private readonly StoreWriter writer;
    private readonly ObjectStore store;
    private readonly DeviceCredential? credential;
    private readonly PeerTable table;
    private readonly LWSEServer server;
    private int stopped;

    private MeshNode(MeshNodeOptions options, MeshNodeEnvironment environment, MeshDatabase database, StoreWriter writer, DeviceCredential? credential, PeerTable table, NodeFacts facts, PairingGate pairing, LWSEServer server)
    {
        this.options = options;
        this.environment = environment;
        this.database = database;
        this.writer = writer;
        this.credential = credential;
        this.table = table;
        this.server = server;
        Facts = facts;
        Pairing = pairing;
        store = new ObjectStore(writer, environment.Files);
    }

    /// <summary>Who this node is; <see langword="null"/> until it has an identity (a device key and its user's Roster).</summary>
    public NodeIdentity? Identity => writer.Trust.Self is { } self && credential is not null ? new NodeIdentity(self.Device, self.User) : null;

    /// <summary>This node's device id, from its device key; <see langword="null"/> while the key store holds none.</summary>
    public DeviceId? Device => credential?.Device;

    /// <summary>Every peer device the node knows of and where it stands, as of the last change.</summary>
    public IReadOnlyList<PeerRecord> Peers => table.View;

    /// <summary>What happened to the node's connections and peers, in order.</summary>
    public NodeFacts Facts { get; }

    /// <summary>
    /// Where the mesh listener is: the bound endpoint (the system's choice when the port was 0), or in memory the port
    /// number on the unspecified address.
    /// </summary>
    public IPEndPoint MeshEndpoint => environment.InMemory
        ? new IPEndPoint(IPAddress.IPv6Any, options.MeshPort)
        : server.Endpoints.FirstOrDefault() ?? new IPEndPoint(IPAddress.IPv6Any, options.MeshPort);

    /// <summary>The node's trust now.</summary>
    internal TrustSnapshot Trust => writer.Trust;

    /// <summary>The node's peer table.</summary>
    internal PeerTable Table => table;

    /// <summary>Whether a pairing is under way, for the TLS layer.</summary>
    internal PairingGate Pairing { get; }

    /// <summary>
    /// Starts a node: opens its store, loads its device key, builds its server with the three ports, binds (or, in
    /// memory, opens) the mesh port and starts dialling the peers discovery names.
    /// </summary>
    /// <param name="options">What the node is told about itself.</param>
    /// <param name="environment">Everything dishonest it needs.</param>
    /// <param name="cancellationToken">Cancels starting; a node that did not start holds nothing.</param>
    /// <returns>The running node.</returns>
    /// <exception cref="MeshStoreException">The store or database cannot be used.</exception>
    /// <exception cref="MeshNodeException">The key store's device key is not the database's device.</exception>
    public static async Task<MeshNode> StartAsync(MeshNodeOptions options, MeshNodeEnvironment environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        TimeProvider time = environment.Time;
        MeshDatabase database = await MeshDatabase.OpenAsync(environment.Database, environment.Entropy, cancellationToken).ConfigureAwait(false);
        var bus = new ChangeBus();
        StoreWriter? writer = null;
        DeviceCredential? credential = null;
        LWSEServer? server = null;
        try
        {
            writer = await StoreWriter.StartAsync(database, environment.Files, StoreOptions.Default, time, bus, new Promises(), cancellationToken).ConfigureAwait(false);
            credential = await LoadCredentialAsync(environment, writer.Trust, cancellationToken).ConfigureAwait(false);

            StoreWriter running = writer;
            DeviceCredential? presented = credential;
            var facts = new NodeFacts(time, environment.Loggers.CreateLogger("Axiom.Mesh.Node"), NodeFacts.DefaultCapacity);
            var table = new PeerTable(() => running.Trust, time, facts);
            var pairing = new PairingGate();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var dialler = new PeerDialler(table, environment.Dialer, facts);
            var pump = new DiscoveryPump(environment.Discovery, table, bus, () => running.Trust);
            var evictor = new Evictor(running, bus);
            var module = new NodeModule(
                new PeerSessionHandler(() => running.Trust, table, facts, time),
                new LocalPortHandler(),
                new NodeObserver(facts),
                [
                    new LoopService("dialler", started.Task, dialler.RunAsync, facts),
                    new LoopService("discovery", started.Task, pump.ObserveAsync, facts),
                    new LoopService("trust", started.Task, pump.FollowTrustAsync, facts),
                    new LoopService("evictor", started.Task, evictor.RunAsync, facts),
                ],
                dialler);
            var tls = new Func<TlsRole, MeshTlsOptions>(role => new MeshTlsOptions { Role = role, Credential = () => presented, Trust = () => running.Trust, Pairing = pairing, Time = time });
            var serverOptions = new ServerOptions
            {
                Admission = new AdmissionOptions { PerClient = Limits.AdmissionPerAddress },
                DrainTimeout = options.DrainTimeout,
            };

            server = LWSEServer.Build(serverOptions, b => Describe(b.Clock(time).AddModule(module), options, environment.InMemory, tls));
            try
            {
                await server.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                started.TrySetCanceled(CancellationToken.None);
                throw;
            }

            started.TrySetResult();
            return new MeshNode(options, environment, database, running, credential, table, facts, pairing, server);
        }
        catch
        {
            if (server is not null)
                await server.DisposeAsync().ConfigureAwait(false);
            credential?.Dispose();
            if (writer is not null)
                await writer.StopAsync().ConfigureAwait(false);
            await database.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Opens an in-memory connection to this node's mesh port, as a peer at <paramref name="remote"/> would by TCP: the
    /// node serves it with the same admission, TLS and protocol. For in-memory meshes.
    /// </summary>
    /// <param name="remote">The address the node sees as the peer's.</param>
    /// <returns>The peer's end of the connection.</returns>
    /// <exception cref="InvalidOperationException">The node binds sockets, or is not running.</exception>
    public Stream ConnectInMemory(IPEndPoint remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        if (!environment.InMemory)
            throw new InvalidOperationException("This node binds sockets; connect to its MeshEndpoint over TCP.");
        return server.ConnectInMemory(options.MeshPort, remote);
    }

    /// <summary>
    /// Makes this device a device of a user, by that user's Roster: what creating an identity or an enrolment ends with
    /// (LOCAL-SPEC.md §6.1). The user becomes known and the Roster is stored; a newer Roster later replaces it.
    /// </summary>
    /// <param name="roster">The user's current Roster; it must list this device's key, not removed.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns><see langword="true"/> when the identity changed.</returns>
    /// <exception cref="MeshNodeException">The node has no device key, or the Roster does not list it.</exception>
    public async Task<bool> AdoptIdentityAsync(VerifiedRoster roster, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roster);
        DeviceCredential own = credential ?? throw new MeshNodeException(
            MeshFault.Caller,
            "This node has no device key, so it cannot become a device of a user.",
            "Store a device key in the node's key store before the node starts.");
        if (!roster.Content.TryGetDevice(own.Device, out RosterDevice? listed) || listed.Removed)
        {
            throw new MeshNodeException(
                MeshFault.Caller,
                $"The Roster of user {roster.Content.User} does not list this device ({own.Device}) as a current device.",
                "Adopt a Roster that lists this device's key and does not remove it.");
        }

        bool changed = await writer.SendAsync(new IdentityCommand(new SelfIdentity(roster.Content.UserKey, own.Device)), cancellationToken).ConfigureAwait(false);
        await store.PutAsync(roster, cancellationToken).ConfigureAwait(false);
        return changed;
    }

    /// <summary>
    /// Knows a user by a Roster delivered by a channel the users trust (SPEC.md §13): what a completed pairing ends with.
    /// The user becomes known, the Roster is stored, and the user's devices are dialled when discovery names them; a
    /// Roster with a higher seq replaces the current one, and a device it removes is closed and refused from then on.
    /// </summary>
    /// <param name="roster">The user's Roster.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns>A task that completes when the Roster is stored and the trust swapped in.</returns>
    public async Task AcceptUserAsync(VerifiedRoster roster, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roster);
        await writer.SendAsync(new PairCommand(roster.Content.UserKey), cancellationToken).ConfigureAwait(false);
        await store.PutAsync(roster, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the node: stops accepting and dialling, lets every session say <c>Bye(Shutdown)</c> within the drain
    /// timeout, stops its loops and the store writer, and releases the database it opened over the caller's. The
    /// caller's database, object files and key store stay the caller's.
    /// </summary>
    /// <param name="cancellationToken">Cuts the drain short.</param>
    /// <returns>A task that completes when everything is released.</returns>
    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
            return;
        try
        {
            await server.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            table.Dispose();
            await server.DisposeAsync().ConfigureAwait(false);
            await writer.StopAsync().ConfigureAwait(false);
            await database.DisposeAsync().ConfigureAwait(false);
            credential?.Dispose();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);

    // The three ports of DESIGN.md §10.2: the mesh port (a number in memory, else bound), the outbound and local ports.
    private static IListeningServerBuilder Describe(IServerBuilder builder, MeshNodeOptions options, bool inMemory, Func<TlsRole, MeshTlsOptions> tls)
    {
        Func<PortBuilder, PortPlan> mesh = p => p.Through(MeshTls.Key, tls(TlsRole.Server)).Speak(Ports.MeshProtocolKey);
        IListeningServerBuilder listening = inMemory ? builder.InMemory().Listen(options.MeshPort, mesh)
            : options.ListenAddress is { } address ? builder.Listen(new IPEndPoint(address, options.MeshPort), mesh)
            : builder.Listen(options.MeshPort, mesh);
        return listening
            .Listen(Ports.Outbound, p => p.Through(MeshTls.Key, tls(TlsRole.Client)).Speak(Ports.MeshProtocolKey))
            .Listen(Ports.Local, p => p.Speak(Ports.LocalProtocolKey));
    }

    // The device key becomes the TLS certificate; it must be the device the database names, if it names one.
    private static async Task<DeviceCredential?> LoadCredentialAsync(MeshNodeEnvironment environment, TrustSnapshot trust, CancellationToken cancellationToken)
    {
        byte[]? pkcs8 = await environment.Keys.ReadAsync(KeyRole.Device, cancellationToken).ConfigureAwait(false);
        if (pkcs8 is null)
            return null;
        DeviceCredential credential;
        try
        {
            credential = DeviceCredential.Create(pkcs8, environment.Time.GetUtcNow());
        }
        catch (ArgumentException ex)
        {
            throw new MeshNodeException(
                MeshFault.Environment,
                "The key store's device key is not a PKCS#8 P-256 key: " + ex.Message,
                "Restore the device key from a backup, or remove it so that the node creates its identity anew.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        if (trust.Self is { } self && self.Device != credential.Device)
        {
            credential.Dispose();
            throw new MeshNodeException(
                MeshFault.Environment,
                $"The key store's device key ({credential.Device}) is not the device this database belongs to ({self.Device}).",
                "Start the node with the key store and the data directory that were made together.");
        }

        return credential;
    }
}
