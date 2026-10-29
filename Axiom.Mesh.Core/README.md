# Axiom.Mesh.Core

The engine of an [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) node: the content-addressed store and its index, trust in rosters and grants, mutual TLS pinned to the rosters, replication with peers, pins and eviction, and the local port every client of the pipe reaches. Everything dishonest (files, database, keys, clock, sockets, discovery) is injected.

Namespace: `Axiom.Mesh.Core` (the store's seams in `Axiom.Mesh.Core.Storage`). Depends on Model, Protocol, Local, Discovery, ProtoStream, LDB and (from LWSE 0.3.0) LWSE Core.

## Status

Phase 1a, in progress. Built: the object store and its LDB index, the trust snapshot, the single store writer, intake with the held buffer, pins and eviction (M5); the transport (M6): `MeshNode.StartAsync`, one LWSE server with the mesh port and the adopt-only outbound and local ports, mutual TLS 1.3 pinned to the rosters, the `Hello` exchange, the duplicate rule, refusals, violations and backoff, the peer table and the dialler. An established session only lives for now: replication (M7) and the local port's requests (M8) follow.

## Quick Start

What a Host or a test hands the engine are its seams: where object files live and where randomness comes from. Both have an in-memory implementation for tests and one for a real node.

```csharp
using Axiom.Mesh;
using Axiom.Mesh.Core;
using Axiom.Mesh.Core.Storage;

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

## Starting a node

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
  pipe's clients (it closes them at once until the local port's requests land). Admission allows 4 connections per peer
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

## What is in it

| Folder | Types |
|---|---|
| `Seams/` | `IEntropy`, `SystemEntropy`, `DeterministicEntropy`, `IKeyStore`, `KeyRole`, `IDialer`, `TcpDialer` (public) |
| `Transport/` | `DeviceCertificate` (public); `MeshTls` (key, ALPN sets), `MeshTlsLayer`, `MeshTlsOptions`, `PeerCheck` (the two-step validation), `PeerIdentity` (the connection feature), `DeviceCredential`, `PairingWindow`, `PairingGate`, `LinkActivity`, `ActivityStream`, `TlsRole` |
| `Node/` | `MeshNodeOptions`, `MeshNodeEnvironment`, `MeshNodeException`, `NodeIdentity`, `NodeFacts`, `NodeFact`, `NodeFactKind` (public); `Ports`, `NodeModule`, `NodeObserver`, `PeerSessionHandler`, `LocalPortHandler`, `LoopService`, `NodeLog` |
| `Peers/` | `PeerRecord`, `PeerState` (public); `PeerTable`, `PeerSession`, `DialPolicy`, `DialDecision`, `SkipReason`, `Backoff`, `ViolationWindow`, `PeerDialler`, `DiscoveryPump`, `DialRequest`, `DialOutcome`, `EstablishVerdict` |
| `Storage/` | `IObjectFiles`, `DirectoryObjectFiles`, `InMemoryObjectFiles`, `ObjectDeletion` (public); the LDB entities (`UserRow`, `DeviceRow`, `PackageRow`, `GrantRow`, `HeadRow`, `EntryRow`, `ObjectRow`, `PinRow`, `PeerRow`, `MetaRow`, `ReferenceRow`), `MeshDatabase` (tables, store epoch, schema version), `Rows<T>` |
| `Trust/` | `TrustSnapshot` (immutable `ITrustSnapshotView`, `TryResolveDevice`, `MayList`, `MayServeGrant`, `MayServeRoster`), `TrustBuilder`, `UserTrust`, `PackageTrust`, `SelfIdentity` |
| `Store/` | `StoreWriter` (the single writer), `StoreBatch`, `PinBook`, the commands, `ObjectStore`, `ObjectHandle`, `Promises`, `Evictor`, `EntryIndex`, `RetentionRule`, `ChunkStreamer` |
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
  bounded ring; take `Facts.LastSequence`, cause something, then `WaitForAsync` the fact after it.
- **The node's clock is the only clock.** Handshake deadlines, heartbeats, idle timeouts, backoffs and retries all run on
  `MeshNodeEnvironment.Time`. With a fake clock, a dial a timer starts runs after `Advance` returns: move the clock in
  steps no longer than the step you wait for, or the same move expires the handshake it started.
