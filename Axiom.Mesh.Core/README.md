# Axiom.Mesh.Core

The engine of an [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) node: the content-addressed store and its index, trust in rosters and grants, mutual TLS pinned to the rosters, replication with peers, pins and eviction, and the local port every client of the pipe reaches. Everything dishonest (files, database, keys, clock, sockets, discovery) is injected.

Namespace: `Axiom.Mesh.Core` (the store's seams in `Axiom.Mesh.Core.Storage`). Depends on Model, Protocol, Local, Discovery, ProtoStream, LDB and (from LWSE 0.3.0) LWSE Core.

## Status

Phase 1a, in progress. Built: the object store and its LDB index, the trust snapshot, the single store writer, intake with the held buffer, pins and eviction (milestone M5). Not yet startable as a node: the transport, replication and the local port follow, and with them `MeshNode.StartAsync`.

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

## What is in it

| Folder | Types |
|---|---|
| `Seams/` | `IEntropy`, `SystemEntropy`, `DeterministicEntropy` (public) |
| `Storage/` | `IObjectFiles`, `DirectoryObjectFiles`, `InMemoryObjectFiles`, `ObjectDeletion` (public); the LDB entities (`UserRow`, `DeviceRow`, `PackageRow`, `GrantRow`, `HeadRow`, `EntryRow`, `ObjectRow`, `PinRow`, `PeerRow`, `MetaRow`, `ReferenceRow`), `MeshDatabase` (tables, store epoch, schema version), `Rows<T>` |
| `Trust/` | `TrustSnapshot` (immutable `ITrustSnapshotView`, `TryResolveDevice`, `MayList`, `MayServeGrant`, `MayServeRoster`), `TrustBuilder`, `UserTrust`, `PackageTrust`, `SelfIdentity` |
| `Store/` | `StoreWriter` (the single writer), `StoreBatch`, `PinBook`, the commands, `ObjectStore`, `ObjectHandle`, `Promises`, `Evictor`, `EntryIndex`, `RetentionRule`, `ChunkStreamer` |
| `Intake/` | `Intake`, `IntakeRules`, `HeldBuffer`, `IWantedSet` |
| `Changes/` | `ChangeBus`, `ChangeSubscription`, `ChangeSet` and its records |
| root | `MeshNode` (not startable yet), `MeshStoreException` |

## Rules of use

- **One writer.** Every change to the store and the index is a command on the `StoreWriter`'s queue; it completes only
  after its batch committed, and a failed one had no effect. Readers read the `TrustSnapshot` and LDB snapshots without
  a lock.
- **Nothing unverified is stored.** The writer takes only a `StorableObject`; intake makes one in the order of SPEC.md
  §10.6 and holds what arrives early, within 8 MiB per sender and 64 MiB in all.
- **Pins decide what stays.** `Head` for the history of followed Packages, `Policy` per the retention table, `Outbound`
  for what this device published until a peer has it, `User` and `Service` for clients; eviction removes unpinned,
  unpromised objects least-recently-used first.
