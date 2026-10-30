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
using LocalDevice = Axiom.Mesh.Local.Device;
using LocalIdentity = Axiom.Mesh.Local.Identity;
using Status = Axiom.Mesh.Local.Status;

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
/// Every established session replicates (SPEC.md §10): the node lists what each peer may see, wants what it follows and
/// lacks, answers what peers want, and pushes every change on at once, so a change crosses any number of hops without
/// polling. <see cref="Replication"/> says where that stands.
/// </para>
/// <para>
/// Start it with <see cref="StartAsync"/>, stop it with <see cref="StopAsync"/>: every session says <c>Bye(Shutdown)</c>
/// before the node lets go of its store, after its local clients heard <c>Drain</c> (<see cref="Local"/>).
/// </para>
/// </remarks>
public sealed class MeshNode : IAsyncDisposable
{
    private readonly MeshNodeOptions options;
    private readonly MeshNodeEnvironment environment;
    private readonly MeshDatabase database;
    private readonly StoreWriter writer;
    private readonly ObjectStore store;
    private readonly CredentialSlot credentials;
    private readonly PeerTable table;
    private readonly LWSEServer server;
    private readonly NodeReplication replication;
    private readonly SemaphoreSlim identityLock = new(1, 1);
    private readonly TaskCompletionSource<NodeIdentity> identityKnown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int userKeyHeld;
    private int stopped;

    private MeshNode(MeshNodeOptions options, MeshNodeEnvironment environment, MeshDatabase database, StoreWriter writer, ObjectStore store, CredentialSlot credentials, PeerTable table, NodeFacts facts, PairingGate pairing, LWSEServer server, NodeReplication replication, LocalPort local, StaticPeerBook statics, bool userKeyHeld)
    {
        StaticPeers = statics;
        this.options = options;
        this.environment = environment;
        this.database = database;
        this.writer = writer;
        this.store = store;
        this.credentials = credentials;
        this.table = table;
        this.server = server;
        this.replication = replication;
        Facts = facts;
        Pairing = pairing;
        Local = local;
        this.userKeyHeld = userKeyHeld ? 1 : 0;
        if (Identity is { } known)
            identityKnown.TrySetResult(known);
    }

    /// <summary>Who this node is; <see langword="null"/> until it has an identity (a device key and its user's Roster).</summary>
    public NodeIdentity? Identity => writer.Trust.Self is { } self && credentials.Credential is not null ? new NodeIdentity(self.Device, self.User) : null;

    /// <summary>This node's device id, from its device key; <see langword="null"/> while the key store holds none.</summary>
    public DeviceId? Device => credentials.Credential?.Device;

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

    /// <summary>Where replication stands now: wants, counters, and each established session's traffic (LOCAL-SPEC.md §5.8).</summary>
    public ReplicationStatus Replication => replication.Status();

    /// <summary>The node's trust now.</summary>
    internal TrustSnapshot Trust => writer.Trust;

    /// <summary>The store writer.</summary>
    internal StoreWriter Writer => writer;

    /// <summary>The object store.</summary>
    internal ObjectStore Store => store;

    /// <summary>What the node's sessions share for replication.</summary>
    internal NodeReplication Replicating => replication;

    /// <summary>The node's peer table.</summary>
    internal PeerTable Table => table;

    /// <summary>The static peers: configured and added (LOCAL-SPEC.md §6.6).</summary>
    internal StaticPeerBook StaticPeers { get; }

    /// <summary>Whether a pairing is under way, for the TLS layer.</summary>
    internal PairingGate Pairing { get; }

    /// <summary>
    /// The local port (LOCAL-SPEC.md): where the Host adopts every pipe connection with its process identity, and where
    /// in-process services connect.
    /// </summary>
    public LocalPort Local { get; }

    /// <summary>Whether the user's private key is on this device (LOCAL-SPEC.md §5.1 <c>userKeyPresent</c>).</summary>
    internal bool UserKeyHeld => Volatile.Read(ref userKeyHeld) != 0;

    /// <summary>The node's object files.</summary>
    internal IObjectFiles Files => environment.Files;

    /// <summary>The node's discovery, whose cache <c>ListDiscovered</c> reads.</summary>
    internal Discovery.IDiscovery Discovery => environment.Discovery;

    /// <summary>The node's clock.</summary>
    internal TimeProvider Time => environment.Time;

    /// <summary>The node's randomness: Package nonces (SPEC.md §2.5).</summary>
    internal IEntropy Entropy => environment.Entropy;

    /// <summary>The device credential and the Entry writer that signs with it.</summary>
    internal CredentialSlot Credentials => credentials;

    /// <summary>
    /// Waits until the node has an identity: at once when it started with one, else when one is created or adopted
    /// (LOCAL-SPEC.md §6.1). The Host starts the discovery that announces the device then.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The identity.</returns>
    public Task<NodeIdentity> WhenIdentityAsync(CancellationToken cancellationToken) => identityKnown.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Creates the identity on the running node (LOCAL-SPEC.md §6.1): the user key and the device key (unless one is held
    /// already) from the node's randomness, both written to the key store; the device credential swapped in so that
    /// handshakes present it from now on; a Roster with seq 1 holding this device, signed with the user key; and the
    /// identity recorded with the Roster stored.
    /// </summary>
    /// <param name="userName">The user's display name, kept on this device.</param>
    /// <param name="deviceName">This device's name in the Roster.</param>
    /// <param name="cancellationToken">Cancels waiting for the key store and the store.</param>
    /// <returns>The identity; <see langword="null"/> when one exists already (<c>AlreadyExists</c>).</returns>
    internal async Task<LocalIdentity?> CreateIdentityAsync(string userName, string deviceName, CancellationToken cancellationToken)
    {
        await identityLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (writer.Trust.Self is not null)
                return null;

            DateTimeOffset now = environment.Time.GetUtcNow();
            DeviceCredential device = credentials.Credential ?? credentials.Install(await CreateDeviceCredentialAsync(now, cancellationToken).ConfigureAwait(false));
            byte[] userKey = KeyFactory.CreatePkcs8(environment.Entropy);
            VerifiedRoster roster;
            try
            {
                using EcdsaSigner user = EcdsaSigner.FromPkcs8(userKey);
                roster = new RosterDraft
                {
                    Seq = 1,
                    Devices = [new RosterDevice(device.Key, deviceName, (ulong)now.ToUnixTimeMilliseconds(), removed: false)],
                }.Sign(user);
                await environment.Keys.WriteAsync(KeyRole.User, userKey, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(userKey);
            }

            Volatile.Write(ref userKeyHeld, 1);
            await AdoptAsync(roster, device, userName, cancellationToken).ConfigureAwait(false);
            return LocalRecords.IdentityOf(writer.Trust, userKeyHeld: true);
        }
        finally
        {
            identityLock.Release();
        }
    }

    /// <summary>
    /// Renames a device of the own user (LOCAL-SPEC.md §6.1): a Roster with seq one higher, signed with the user key and
    /// stored, which the node pushes to every established peer (SPEC.md §7.6).
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="name">Its new name.</param>
    /// <param name="cancellationToken">Cancels waiting for the key store and the store.</param>
    /// <returns>Ok with the device; <c>UserKeyAbsent</c>, <c>NotFound</c> for a device not the own user's, <c>Invalid</c> for a removed one.</returns>
    internal async Task<(Status Status, LocalDevice? Device)> RenameDeviceAsync(DeviceId device, string name, CancellationToken cancellationToken)
    {
        await identityLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TrustSnapshot trust = writer.Trust;
            if (trust.Self is not { } self || !trust.TryGetUser(self.User, out UserTrust? user) || user.Roster is not { } current)
                return (Status.NoIdentity, null);
            byte[]? userKey = await environment.Keys.ReadAsync(KeyRole.User, cancellationToken).ConfigureAwait(false);
            if (userKey is null)
                return (Status.UserKeyAbsent, null);

            VerifiedRoster renamed;
            try
            {
                if (!current.Content.TryGetDevice(device, out _))
                    return (Status.NotFound, null);
                if (user.Devices.Any(d => d.Device.Id == device && d.IsRemoved))
                    return (Status.Invalid, null);
                using EcdsaSigner signer = EcdsaSigner.FromPkcs8(userKey);
                renamed = new RosterDraft
                {
                    Seq = current.Content.Seq + 1,
                    Devices = [.. current.Content.Devices.Select(d => d.Id == device ? new RosterDevice(d.Key, name, d.AddedAt, d.Removed) : d)],
                }.Sign(signer);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(userKey);
            }

            await store.PutAsync(renamed, cancellationToken).ConfigureAwait(false);
            TrustSnapshot now = writer.Trust;
            UserTrust after = now.TryGetUser(self.User, out UserTrust? stored) ? stored : user;
            EffectiveDevice effective = after.Devices.FirstOrDefault(d => d.Device.Id == device);
            return (Status.Ok, LocalRecords.DeviceOf(effective, self.User, self.Device, table.Find(device)?.LastSeen));
        }
        finally
        {
            identityLock.Release();
        }
    }

    /// <summary>
    /// Runs work that signs with the user key (LOCAL-SPEC.md §2.2, §6): under the node's identity lock, so that two such
    /// requests never choose the same sequence number, with the key read from the key store for this call only and wiped
    /// after it. The work reads trust itself, inside the lock.
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="work">The work: the user key's signer in, a status and a value out.</param>
    /// <param name="cancellationToken">Cancels waiting for the lock, the key store and the store.</param>
    /// <returns><c>NoIdentity</c> or <c>UserKeyAbsent</c> before the work ran; else what the work returned.</returns>
    internal async Task<(Status Status, T? Value)> WithUserKeyAsync<T>(Func<EcdsaSigner, CancellationToken, Task<(Status, T?)>> work, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(work);
        await identityLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (writer.Trust.Self is null)
                return (Status.NoIdentity, null);
            byte[]? userKey = await environment.Keys.ReadAsync(KeyRole.User, cancellationToken).ConfigureAwait(false);
            if (userKey is null)
                return (Status.UserKeyAbsent, null);
            try
            {
                using EcdsaSigner signer = EcdsaSigner.FromPkcs8(userKey);
                return await work(signer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(userKey);
            }
        }
        finally
        {
            identityLock.Release();
        }
    }

    // Records the identity and stores the Roster; the identity is known from then on.
    private async Task<bool> AdoptAsync(VerifiedRoster roster, DeviceCredential own, string userName, CancellationToken cancellationToken)
    {
        var identity = new SelfIdentity(roster.Content.UserKey, own.Device)
        {
            UserName = userName,
            CreatedMs = environment.Time.GetUtcNow().ToUnixTimeMilliseconds(),
        };
        bool changed = await writer.SendAsync(new IdentityCommand(identity), cancellationToken).ConfigureAwait(false);
        await store.PutAsync(roster, cancellationToken).ConfigureAwait(false);
        if (Identity is { } known)
            identityKnown.TrySetResult(known);
        return changed;
    }

    // The device key: the one the key store holds (an earlier ShowDevice pairing made it), else a new one, written first.
    private async Task<DeviceCredential> CreateDeviceCredentialAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        byte[] pkcs8 = await environment.Keys.ReadAsync(KeyRole.Device, cancellationToken).ConfigureAwait(false) ?? KeyFactory.CreatePkcs8(environment.Entropy);
        try
        {
            await environment.Keys.WriteAsync(KeyRole.Device, pkcs8, cancellationToken).ConfigureAwait(false);
            return DeviceCredential.Create(pkcs8, now);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    private static async Task<bool> HoldsUserKeyAsync(IKeyStore keys, CancellationToken cancellationToken)
    {
        byte[]? key = await keys.ReadAsync(KeyRole.User, cancellationToken).ConfigureAwait(false);
        if (key is null)
            return false;
        CryptographicOperations.ZeroMemory(key);
        return true;
    }

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
        CredentialSlot? slot = null;
        LWSEServer? server = null;
        NodeReplication? replication = null;
        try
        {
            StoreOptions storeOptions = StoreOptions.Default with { BudgetBytes = options.StoreBudgetBytes > 0 ? options.StoreBudgetBytes : long.MaxValue };
            writer = await StoreWriter.StartAsync(database, environment.Files, storeOptions, time, bus, new Promises(), cancellationToken).ConfigureAwait(false);
            credential = await LoadCredentialAsync(environment, writer.Trust, cancellationToken).ConfigureAwait(false);
            bool userKeyHeld = await HoldsUserKeyAsync(environment.Keys, cancellationToken).ConfigureAwait(false);

            StoreWriter running = writer;
            slot = new CredentialSlot(running, credential);
            credential = null;
            CredentialSlot credentials = slot;
            var facts = new NodeFacts(time, environment.Loggers.CreateLogger("Axiom.Mesh.Node"), NodeFacts.DefaultCapacity);
            var table = new PeerTable(() => running.Trust, time, facts);
            var pairing = new PairingGate();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var dialler = new PeerDialler(table, environment.Dialer, facts);
            var pump = new DiscoveryPump(environment.Discovery, table, bus, () => running.Trust);
            var evictor = new Evictor(running, bus);
            var store = new ObjectStore(running, environment.Files);
            replication = new NodeReplication(running, bus, store, table, facts, time);
            NodeReplication replicating = replication;
            var local = new LocalPort(options.Local, facts, time, environment.Loggers.CreateLogger("Axiom.Mesh.Local"), bus);
            StaticPeerBook statics = await StaticPeerBook.OpenAsync(options.StaticPeers, running, table, time, cancellationToken).ConfigureAwait(false);
            table.OnChanged(local.Hub.PeersChanged);
            replicating.Transfers.OnRecorded(local.Hub.TransferChanged);
            var module = new NodeModule(
                new PeerSessionHandler(() => running.Trust, table, facts, time, replicating),
                local.Handler,
                new NodeObserver(facts),
                [
                    new LoopService("dialler", started.Task, dialler.RunAsync, facts),
                    new LoopService("discovery", started.Task, pump.ObserveAsync, facts),
                    new LoopService("trust", started.Task, pump.FollowTrustAsync, facts),
                    new LoopService("evictor", started.Task, evictor.RunAsync, facts),
                    new LoopService("replicator", started.Task, replicating.Replicator.RunAsync, facts),
                    new LoopService("local events", started.Task, local.Hub.RunChangesAsync, facts),
                    new LoopService("local peers", started.Task, local.Hub.RunPeersAsync, facts),
                    new LoopService("static peers", started.Task, statics.RunAsync, facts),
                ],
                dialler,
                local);
            var tls = new Func<TlsRole, MeshTlsOptions>(role => new MeshTlsOptions { Role = role, Credential = () => credentials.Credential, Trust = () => running.Trust, Pairing = pairing, Time = time });
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
            var node = new MeshNode(options, environment, database, running, store, credentials, table, facts, pairing, server, replicating, local, statics, userKeyHeld);
            local.Attach(node);
            return node;
        }
        catch
        {
            if (server is not null)
                await server.DisposeAsync().ConfigureAwait(false);
            replication?.Dispose();
            credential?.Dispose();
            slot?.Dispose();
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
        DeviceCredential own = credentials.Credential ?? throw new MeshNodeException(
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

        return await AdoptAsync(roster, own, string.Empty, cancellationToken).ConfigureAwait(false);
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
    /// Follows a Package (LOCAL-SPEC.md §6.3): its Manifest, Grants and Entries are replicated and pinned from now on, and
    /// its payload objects as far as its retention policy goes (SPEC.md §10.2, §11.2). Following a Package nobody listed
    /// yet waits for its Manifest from the first peer that lists it.
    /// </summary>
    /// <param name="package">The Package.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns><see langword="true"/> when the node did not follow it before.</returns>
    public Task<bool> FollowAsync(PackageId package, CancellationToken cancellationToken) =>
        writer.SendAsync(new PackageCommand(PackageCommandKind.Follow, package, LocalRetention.Default), cancellationToken);

    /// <summary>
    /// Stops following a Package (LOCAL-SPEC.md §6.3): it is listed again, its wants are cancelled, and what it pinned for
    /// history and policy may be evicted; what this device wrote stays pinned until sent once (SPEC.md §11.2). The own
    /// Packages cannot be unfollowed.
    /// </summary>
    /// <param name="package">The Package.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns><see langword="true"/> when the node followed it before.</returns>
    public Task<bool> UnfollowAsync(PackageId package, CancellationToken cancellationToken) =>
        writer.SendAsync(new PackageCommand(PackageCommandKind.Unfollow, package, LocalRetention.Default), cancellationToken);

    /// <summary>
    /// Stores an object made here (SPEC.md §11.1): a Manifest or Grant the own user signed, a Chunk, ChunkList or Tree of
    /// a file about to be published. Only a <see cref="StorableObject"/> is taken, so only what verified is stored; a
    /// Manifest is kept only for a followed Package, a Grant only with its Manifest.
    /// </summary>
    /// <param name="storable">The object.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns><see langword="true"/> when the object is stored now, whether by this call or before.</returns>
    public async Task<bool> PutAsync(StorableObject storable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storable);
        return (await store.PutAsync(storable, cancellationToken).ConfigureAwait(false)).IsStored;
    }

    /// <summary>
    /// Writes an Entry on this device (SPEC.md §9.3; LOCAL-SPEC.md §9.4): clock and parents chosen by the parent rule,
    /// signed with the device key, stored, and pushed to every peer that may see the Package. Store the objects the payload
    /// names first; they stay pinned <c>Outbound</c> until a peer fetched each once (SPEC.md §11.2).
    /// </summary>
    /// <param name="package">The Package; followed, with its Manifest held, and the device's user its owner or a Write grantee.</param>
    /// <param name="payload">What the Entry says.</param>
    /// <param name="cancellationToken">Cancels waiting for the store.</param>
    /// <returns>The Entry's hash.</returns>
    /// <exception cref="MeshNodeException">The node has no identity, does not follow the Package, may not write to it, or its store is full.</exception>
    public Task<ObjectHash> WriteEntryAsync(PackageId package, EntryPayload payload, CancellationToken cancellationToken) =>
        (credentials.Entries ?? throw new MeshNodeException(MeshFault.Caller, "This node has no device key, so it cannot sign an Entry.", "Store a device key in the node's key store before the node starts."))
            .WriteAsync(package, payload, cancellationToken);

    /// <summary>Whether the node holds an object, as of the last committed batch.</summary>
    /// <param name="hash">The object's hash.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns><see langword="true"/> when stored.</returns>
    public Task<bool> ContainsAsync(ObjectHash hash, CancellationToken cancellationToken) => store.ContainsAsync(hash, cancellationToken);

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
            await Local.DrainAsync(cancellationToken).ConfigureAwait(false);
            await server.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            table.Dispose();
            await server.DisposeAsync().ConfigureAwait(false);
            replication.Dispose();
            await writer.StopAsync().ConfigureAwait(false);
            await database.DisposeAsync().ConfigureAwait(false);
            credentials.Dispose();
            identityLock.Dispose();
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
