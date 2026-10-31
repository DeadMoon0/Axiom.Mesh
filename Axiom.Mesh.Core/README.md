# Axiom.Mesh.Core

The engine of an [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) node: the content-addressed store and its index, trust in rosters and grants, mutual TLS pinned to the rosters, replication with peers, pins and eviction, and the local port every client of the pipe reaches. Everything dishonest (files, database, keys, clock, sockets, discovery) is injected.

Namespace: `Axiom.Mesh.Core` (the store's seams in `Axiom.Mesh.Core.Storage`). Depends on Model, Protocol, Local, Discovery, ProtoStream, LDB and (from LWSE 0.3.0) LWSE Core.

## Status

0.1.0, phase 1a. The whole engine of the fundamentals plan: the store and its index, trust, intake, pins and eviction;
the transport (mutual TLS 1.3 pinned to the rosters, the `Hello` exchange, duplicates, backoff, the peer table);
replication (listings, wants, push and relay, flow control, faulty-content and violation bookkeeping, late parents);
the local port with the phase-1 request set, subscriptions, operations and `Fetch`; the pairing shell of PAIR-SPEC.md in
all four modes. The node reads an Entry's envelope only (`references`, `redacts`, SPEC.md draft 5) and parses no
payload; cutting files and writing Trees to disk is the client's work (`StoreFiles`, `Trees` in Axiom.Mesh.Local). Since
0.1.0 the index is schema version 2 (a store of a pre-release build is refused at start with the steps out), a dropped
Package is announced (`PackageChanged(Dropped)`, LOCAL-SPEC.md §14.12), and on Linux and macOS the store directory is
fsynced after each rename (`DirectoryObjectFiles.ProbeDirectoryFlush` says how renames are made durable here).

## Quick Start: embedding a node

A node is started once, with everything dishonest handed in: object files, the database, the key store, discovery, the
dialler, the clock, randomness and logging. This is what the Host does on disk and sockets; `Axiom.Mesh.Testing` does the
same in memory, with a fake clock, for any number of nodes.

```csharp
using System.Net;
using Axiom.LDB;
using Axiom.Mesh.Discovery;
using Microsoft.Extensions.Logging.Abstractions;

string home = Path.Combine(Path.GetTempPath(), "axiom-mesh-node");
TimeProvider time = TimeProvider.System;
var discovery = new CompositeDiscovery(new CompositeDiscoveryOptions
{
    Parts = [new StaticPeers(new StaticPeersOptions
    {
        Peers = [new IPEndPoint(IPAddress.Parse("192.168.1.20"), MeshNodeOptions.DefaultMeshPort)],
        Time = time,
        Facts = new DiscoveryFacts(256),
    })],
    Time = time,
    Cache = new AnnouncementCache(1024, TimeSpan.FromSeconds(60)),
});

await using Database database = await Database.OpenAsync(Path.Combine(home, "index"), DatabaseOptions.Default with { TimeProvider = time }, CancellationToken.None);
await using MeshNode node = await MeshNode.StartAsync(MeshNodeOptions.Default, new MeshNodeEnvironment
{
    Files = new DirectoryObjectFiles(home, new SystemEntropy()),
    Database = database,
    Keys = new NoKeysYet(),
    Discovery = discovery,
    Dialer = new TcpDialer(time),
    Time = time,
    Entropy = new SystemEntropy(),
    Loggers = NullLoggerFactory.Instance,
}, CancellationToken.None);

Console.WriteLine(node.MeshEndpoint);       // [::]:41810, bound on every address
Console.WriteLine(node.Identity is null);   // True: without a device key there is no certificate and no Hello yet
foreach (PeerRecord peer in node.Peers)
    Console.WriteLine(peer);                // each peer device: its state, sessions, failures, backoff

// What happened to connections and peers, in order; wait for a fact instead of polling or sleeping.
using var patience = new CancellationTokenSource(TimeSpan.FromMinutes(1));
NodeFact established = await node.Facts.WaitForAsync(f => f.Kind == NodeFactKind.Established, 0, patience.Token);

// The Host keeps keys in files wrapped for the user; a node without one runs, but handshakes nobody.
sealed class NoKeysYet : IKeyStore
{
    public ValueTask<byte[]?> ReadAsync(KeyRole role, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);

    public ValueTask WriteAsync(KeyRole role, ReadOnlyMemory<byte> pkcs8, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This sample keeps no keys.");
}
```

What a started node does on its own:

- **Three ports, one server** (DESIGN.md §10.2): the mesh port (41810, or a number in memory) with mutual TLS as server,
  the adopt-only outbound port with mutual TLS as client for every peer it dials, and the adopt-only local port for the
  pipe's clients and in-process services (see "The local port" below). Admission allows 4 connections per peer
  address (SPEC.md §4.6).
- **Mutual TLS 1.3 pinned to the rosters** (SPEC.md §4.2, §4.3): a client certificate is required, ALPN `axiom-mesh/1`
  must be negotiated, no session resumption, no renegotiation, no chain or revocation check; the peer's key must be a
  not-removed device of a known user's current Roster, read from the trust snapshot at that handshake. Anything else is
  closed without a protocol byte. The handshake must complete within 10 seconds.
- **Hello and the duplicate rule** (SPEC.md §6, §7.1, §4.5): each side sends `Hello` at once; the peer's is checked with
  `HelloRule.Check` and refused with `Bye(Unauthorized)`; one session per device survives, the other hears
  `Bye(Duplicate)`. A device a newer Roster removes is closed with `Bye(Unauthorized)`; a stopping node says
  `Bye(Shutdown)`.
- **Dialling** (SPEC.md §3, §4.4, §6.2, §12.1): discovery's announcements are dialled only for not-removed devices of
  known users; static peers are dialled and the handshake decides. A failed dial, or a close before the peer's `Hello`,
  backs off 10 seconds doubling to 5 minutes, then the node redials on its own; three violations within an hour hold
  dials back for an hour.

## The store's seams

What a Host or a test hands the engine are its seams: where object files live and where randomness comes from. Both have an in-memory implementation for tests and one for a real node.

```csharp
using Axiom.Mesh;
using Axiom.Mesh.Core;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Payloads;

// In memory, reproducible: what a test or an in-memory mesh uses. A real node uses
// new DirectoryObjectFiles(storePath, new SystemEntropy()) and SystemEntropy.
IEntropy entropy = new DeterministicEntropy(42);
IObjectFiles files = new InMemoryObjectFiles();

// Only verified bytes reach the files: a Chunk names itself by its hash.
Chunk chunk = ObjectBuilders.Chunk("hello"u8);
bool written = await files.WriteAtomicAsync(chunk.Hash, chunk.Bytes, CancellationToken.None);   // true
bool again = await files.WriteAtomicAsync(chunk.Hash, chunk.Bytes, CancellationToken.None);     // false: idempotent

// A reader keeps the bytes it opened even when the store deletes the name meanwhile (LOCAL-SPEC.md §9.7).
if (files.TryOpenRead(chunk.Hash, out Stream? reader))
{
    using (reader)
    {
        Console.WriteLine(files.TryDelete(chunk.Hash));   // Deleted
        Console.WriteLine(reader.Length);                   // 7: tag, version and five bytes of data
    }
}

byte[] nonce = new byte[32];
entropy.Fill(nonce);                                         // the same bytes on every run with seed 42
```

On disk, an object with hash `h` is the file `<store>/objects/<h[0..1]>/<h[2..3]>/<h>` holding exactly its bytes, written to a temporary name in the same directory, flushed and renamed into place; a crash leaves it absent or complete.

## Replicating

Once two nodes are established, replication runs by itself (SPEC.md §10). Each side lists the Packages the other's user
may see; a node wants what it follows and lacks (the Manifest first, then Grants, then Heads from devices that may
write, then parents until it reaches Entries it holds, then the objects Entries reference as far as the retention policy goes),
answers what the peer wants after checking the peer may see it, and pushes every change (`PackageChanged`, `Have`) to
every established peer but the one it came from, so a change crosses any number of hops without polling. A node's own
writes go through the same store:

```csharp
static class Replicating
{
    // Follow a Channel, write a Message on this device, and see what the sessions carried.
    public static async Task<ObjectHash> WriteAsync(MeshNode node, PackageId channel, CancellationToken cancellationToken)
    {
        await node.FollowAsync(channel, cancellationToken);
        ObjectHash written = await node.WriteEntryAsync(
            channel,
            new MessagePayload { Text = "hello", Attachments = [], ReplyTo = default }.ToBody(),
            cancellationToken);

        ReplicationStatus status = node.Replication;
        Console.WriteLine($"{status.WantsOutstanding} wants outstanding, {status.ObjectsOut} objects sent");
        foreach (SessionTraffic session in status.Sessions)
            Console.WriteLine(session);   // bytes and objects each way, wants each way, queued object bytes
        return written;
    }
}
```

What replication enforces of the peer: a page out of sequence, a listing over 65 536 Packages or slower than 60 seconds,
a `PackageChanged` between pages, a `Want` past 64 outstanding, an `Object` whose bytes are not its hash, and a frame not
completed within 30 seconds of its first byte are violations (`Bye(Violation)`, counted against the device). Content that
is well-formed but wrong, such as an Entry whose clock jumps more than 2^32 above its parent's, is faulty: dropped,
recorded against the device (`PeerRecord.Faulty`, a `FaultyContent` fact), never forwarded, and the session goes on.
Toward the peer, at most 8 MiB of objects wait to be sent; answers past that are deferred while every other message
flows.

## The local port

`MeshNode.Local` is where every client talks to the node over `axiom-mesh-local/1` (LOCAL-SPEC.md). The Host adopts each
pipe connection with the process identity it read from the operating system (`Local.AdoptAsync(stream,
ProcessIdentity.Pipe(pid, user, executable), ct)`); an in-process service connects with `Local.ConnectInProcess(name)`
and hands the stream to `LocalClient.OpenAsync`. `MeshNodeOptions.Local` (`LocalPortOptions`) lists the services by name
and provenance (in process, spawned or external with a token), the executables the user-key requests are confined to,
the user the Host serves, and the drain's grace.

- **Hello** (§4.2): version, role, a listed service name proven by its provenance (an in-process name only on its own
  pair; spawned and external tokens compared in constant time), one connection per name (the live one is kept), at
  most 64 user connections and 16 waiting for `Hello`.
- **Requests** (§3.4, §6): every request gets exactly one response. Before a handler runs, the dispatcher checks the
  role, the identity (`NoIdentity`), `Draining`, and the confinement of user-key requests, each of which is logged with
  the connection's pid and executable. The node serves the phase-1 set of the fundamentals plan, pairing included;
  `UpdatePackage`, `ServiceCall`, `AddTelemetry` and `Emit` answer `UnknownRequest`, the telemetry queries `NotFound`.
- **User-key requests** (§2.2): `CreatePackage`, `SetGrant`, `RevokeGrant`, `RemoveDevice` and `RenameDevice` sign with
  the user key inside the node, read from the `IKeyStore` for the one request and wiped after it; a device without it
  answers `UserKeyAbsent`.
- **Operations** (§8.6): `Fetch` wants from the peers that list the Package and fails `NotFound` after 60 s without a
  source; it can be cancelled. `Materialise` and `PutFile` left the node in LOCAL-SPEC.md draft 4: cutting files and
  writing Trees to disk is the client's work (`StoreFiles` and `Trees` in Axiom.Mesh.Local).
- **Service role** (§9): `RegisterKind` within the service's configured kinds, one holder per kind; `WriteEntry` parses
  no payload: it copies `references` and `redacts` into the envelope, checks every reference is stored and the named
  parents, and chooses clock and parents as §9.4 says;
  `PutObject` stores unsigned objects; `GetStorePath` names the store for direct reads (Host capability bit 0).
- **Subscriptions** (§7): the response precedes the first event; a replay from a store sequence reads one Entry at a time
  (more than 256 is `GapTooLarge`); a subscription lapses alone at 256 events or 16 MiB; a connection holding 64 MiB its
  client does not read, or a frame not accepted within 30 s, is closed `Bye(Unresponsive)` (256 MiB across connections).
- **Liveness and drain** (§4.4, §4.5): `Ping` after 60 s of silence, `Bye(Unresponsive)` 30 s later; `MeshNode.StopAsync`
  sends `Drain` first and `Bye(Shutdown)` when the grace elapsed or every client left.
- **Identity** (§6.1): `CreateIdentity` makes the keys from the node's `IEntropy`, writes them to the `IKeyStore`, swaps
  the device credential in and adopts a Roster with seq 1; `MeshNode.WhenIdentityAsync` completes then.

## What is in it

| Folder | Types |
|---|---|
| `Seams/` | `IEntropy`, `SystemEntropy`, `DeterministicEntropy`, `IKeyStore`, `KeyRole`, `IDialer`, `TcpDialer` (public) |
| `Transport/` | `DeviceCertificate` (public); `MeshTls` (key, ALPN sets), `MeshTlsLayer`, `MeshTlsOptions`, `PeerCheck` (the two-step validation), `PeerIdentity` (the connection feature), `DeviceCredential`, `PairingWindow`, `PairingGate`, `LinkActivity`, `ActivityStream`, `TlsRole` |
| `Node/` | `MeshNodeOptions`, `MeshNodeEnvironment`, `MeshNodeException`, `NodeIdentity`, `NodeFacts`, `NodeFact`, `NodeFactKind` (public); `Ports`, `NodeModule`, `NodeObserver`, `PeerSessionHandler`, `LoopService`, `NodeLog` |
| `Local/` | `LocalPort`, `LocalPortOptions`, `LocalService`, `ServiceProvenance`, `ProcessIdentity`, `ProcessKind` (public); `LocalPortHandler`, `LocalConnection` (the per-connection actor), `ConnectionBook`, `LocalHelloRule`, `DispatchRule`, `RequestRoutes` (the dispatcher's table), `Outbox`, `LocalSubscription`, `FilterRule`, `OperationTable`, `LivenessRule`, `LocalEventHub`, the request handlers (`IdentityRequests`, `NodeRequests`, `SubscriptionRequests`, `OperationRequests`) and the record builders |
| `Replication/` | `ReplicationStatus`, `SessionTraffic` (public); `ReplicationSession` (one per established session: read, write and push loops), `SessionOutbox` (control and object lanes), `ReplicationPlanner` (pure: `Plan`, `Walk`), `WantScheduler` (sources, 64 per peer, re-ask after 30 s; `IWantedSet`), `SourceBook`, `Listings` (pure), `ServedObjects` (visibility of a wanted hash), `Replicator` (the node's walk, held-buffer expiry and cancellations), `EntryWriter`, `NodeReplication`, `TelemetrySink`, `ObjectLinks` |
| `Peers/` | `PeerRecord`, `PeerState` (public); `PeerTable`, `PeerSession`, `DialPolicy`, `DialDecision`, `SkipReason`, `Backoff`, `ViolationWindow`, `PeerDialler`, `DiscoveryPump`, `DialRequest`, `DialOutcome`, `EstablishVerdict` |
| `Storage/` | `IObjectFiles`, `DirectoryObjectFiles`, `InMemoryObjectFiles`, `ObjectDeletion`, `StoreDirectoryFlush` (public); `DirectorySync` (the directory fsync through libc), the LDB entities (`UserRow`, `DeviceRow`, `PackageRow`, `GrantRow`, `HeadRow`, `EntryRow`, `ObjectRow`, `PinRow`, `PeerRow`, `MetaRow`, `ReferenceRow`, `ParentRow`), `MeshDatabase` (tables, store epoch, schema version 2), `Rows<T>` |
| `Trust/` | `TrustSnapshot` (immutable `ITrustSnapshotView`, `TryResolveDevice`, `MayList`, `MayServeGrant`, `MayServeRoster`, `TryGetCurrent`), `TrustBuilder`, `UserTrust`, `PackageTrust`, `SelfIdentity`, `CurrentSigned` |
| `Store/` | `StoreWriter` (the single writer), `StoreBatch`, `PinBook`, the commands, `ObjectStore`, `ObjectHandle`, `Promises`, `Evictor`, `EntryIndex`, `HeadsView`, `RetentionRule`, `FileWrites` |
| `Intake/` | `Intake`, `IntakeRules`, `HeldBuffer`, `IWantedSet` |
| `Changes/` | `ChangeBus`, `ChangeSubscription`, `ChangeSet` and its records |
| root | `MeshNode`, `MeshStoreException` |

## Rules of use

- **One writer.** Every change to the store and the index is a command on the `StoreWriter`'s queue; it completes only
  after its batch committed, and a failed one had no effect. Readers read the `TrustSnapshot` and LDB snapshots without
  a lock.
- **Nothing unverified is stored.** The writer takes only a `StorableObject`; intake makes one in the order of SPEC.md
  §10.6 and holds what arrives early, within 8 MiB per sender and 64 MiB in all.
- **Pins decide what stays.** `Head` for the history of followed Packages, `Policy` per the retention table, `Outbound`
  for what this device published until a peer has it, `User` and `Service` for clients; eviction removes unpinned,
  unpromised objects least-recently-used first.
- **Trust comes in by the node's two doors only.** `AdoptIdentityAsync` makes this device a device of a user by that
  user's Roster (what creating an identity or an enrolment ends with); `AcceptUserAsync` knows another user by a Roster
  delivered out of band (what a pairing ends with). Either swaps a new trust snapshot in; the next handshake reads it,
  sessions of devices it no longer lists are closed, and devices discovery heard before are dialled.
- **Facts, not sleeps.** Everything a node decides about connections and peers is a `NodeFact`, logged once and kept in a
  bounded ring; take `Facts.LastSequence`, cause something, then `WaitForAsync` the fact after it. Replication adds
  `ListingDone`, `ObjectStored`, `WantAnswered` and `FaultyContent`, with the object's hash in `NodeFact.Subject`.
- **Write through the node.** `WriteEntryAsync` chooses clock and parents with the parent rule, signs with the device key
  and admits the Entry like any other; it never reads the payload. Store the objects the body references first
  (`PutAsync`), so they are pinned `Outbound` until a peer fetched each once.
- **The node's clock is the only clock.** Handshake deadlines, heartbeats, idle timeouts, backoffs and retries all run on
  `MeshNodeEnvironment.Time`. With a fake clock, a dial a timer starts runs after `Advance` returns: move the clock in
  steps no longer than the step you wait for, or the same move expires the handshake it started.
