# Axiom.Mesh — Design

> Status: design, nothing implemented. This document is the contract the first implementation is built against.
> Where it names a number (a port, a chunk size, a limit) the number is a starting value, not a promise.

## 1. Introduction and goals

Axiom.Mesh is a base for software that runs between the devices on a local network without a server. It
finds the other devices, authenticates them, replicates **Packages** between them and lets **services** run
on top: file synchronisation and chat first, anything local later. It is part of the
[Axiom](https://github.com/DeadMoon0/Axiom) family; packages and namespaces are `Axiom.Mesh*`.

| Goal | Meaning |
|---|---|
| Serverless | Nothing central. Any device that holds data can hand it out; a device that is offline is not missed. |
| Secure by default | Every connection is mutually authenticated and encrypted. Every object is content-addressed; every entry is signed. A device never trusts a peer for anything it can verify itself. |
| Private by default | A Package is visible only to the users its owner granted. Discovery reveals nothing but that a mesh node exists. |
| One engine, many services | Core replicates, stores, pins and access-checks Packages without understanding their content. A service defines a Package kind and its entries; it never touches the wire or the store directly. |
| Usable by others | Pairing is a QR code or a six-word code. Nothing needs a config file. Sensible defaults on every limit. |
| Phones are peers | A phone is a first-class node with a smaller retention policy, not a client of a PC. |

## 2. Constraints

- .NET 8 and .NET 10, trimmable and Native AOT compatible, like every Axiom package. Native AOT means no plugin
  assemblies loaded at runtime: first-party services are compiled in, third parties use the local API (§10.4).
- Built on the Axiom family: [ProtoStream](https://github.com/DeadMoon0/Axiom.ProtoStream) owns the wire,
  [LWSE](https://github.com/DeadMoon0/LWSE) is the host (ports, transports, admission, modules),
  [LDB](https://github.com/DeadMoon0/LDB) holds state. No other runtime dependency.
- Cryptography from `System.Security.Cryptography` only: SHA-256 for content addresses, ECDSA P-256 for
  signatures and for the TLS certificates, TLS 1.3 via `SslStream`. One key type serves both signing and
  transport, and all of it runs on Windows, Linux, macOS, Android and iOS without a native library. (Ed25519
  would be the nicer signature scheme; it is not in the BCL and would cost a dependency. Revisit when it is.)
- Local network only. No NAT traversal, no relays, no internet rendezvous. Nodes that cannot see each other's
  multicast do not find each other; that is accepted.
- The public API follows the Axiom conventions: no optional parameters, no positional records without
  `[ClosedShape]`, no bare numbers, honest layers below one dishonest runtime (see §10).

## 3. Context and scope

In scope: discovery on the LAN, pairing between users, authenticated encrypted transport, the object store,
the Package model, replication, retention, the two first services (Sync, Chat), a management API and UI, and
apps for desktop and mobile.

Out of scope for the first releases: WAN connectivity, encryption of Package content at rest (designed for,
not built; §7.4), three-way content merges, multi-parent history beyond merge entries, running as a system
service on iOS (the platform does not allow it).

## 4. Terms

| Term | Meaning |
|---|---|
| **User** | A person. Identified by a long-lived signing key. |
| **Device** | One installation of a node, owned by one user. Identified by its own signing key. |
| **Node** | A running device: the process that discovers, connects, stores and serves. |
| **Roster** | The signed list of a user's devices. |
| **Grant** | The owner's signed statement that a user may read a Package. |
| **Package** | The unit of sharing: an identity plus a history of signed entries. Always spelled out; never "pack". |
| **Kind** | What a Package's entries mean. `Archive` (files and programs), `Channel` (chat). A service defines a kind. |
| **Entry** | One signed step in a Package's history: a `Version` in an Archive, a `Message` in a Channel. |
| **Object** | Anything in the content-addressed store: a chunk, a tree, an entry. Named by its SHA-256. |
| **Head** | The latest entry a device has written to a Package. A Package has one head per writing device. |
| **Pin** | The local decision to keep an object. Unpinned objects may be evicted and fetched again on demand. |
| **Service** | A module on top of Core that defines a kind and the user-facing behaviour for it. |

## 5. Identities and trust

### 5.1 Three identities

```
User  (ECDSA P-256 key, long-lived)
 └── Device (ECDSA P-256 key per install)   ← signed into the user's Roster
Package (owned by a User)                   ← entries signed by that user's Devices
```

- **User id** = SHA-256 of the user's public key. A user is created once, on the first device; the key never
  leaves that device except through a user-driven backup (an encrypted key file with a passphrase).
- **Device id** = SHA-256 of the device's public key. Created at install.
- **Roster** = `{ userId, devices: [{deviceId, publicKey, name, addedAt}], seq }`, signed by the user key. Adding a
  device raises `seq`. A peer accepts a roster only with a higher `seq` than the one it holds. Removing a
  device is a new roster without it; peers stop talking to that device as soon as the roster reaches them.
- **Package id** = SHA-256 of `{ ownerUserId, created nonce }`, fixed at creation. Names are metadata and may
  change; ids never do.

A user's own devices therefore trust each other through the roster, not through pairwise pairing. Pairing is
only between **users**.

### 5.2 Pairing two users

1. Alice shows a QR code (or reads out a six-word code) containing her user public key and the address of the
   device she is on.
2. Bob's device connects, both sides see a short authentication string derived from both public keys, and both
   confirm it matches on screen. A hostile LAN cannot substitute a key without the strings differing.
3. Both devices store the other user's public key as **known**. Each user's roster then flows over any
   connection, so every device of Alice knows every device of Bob.

Unknown users are refused at the TLS handshake (§6.3). There is no "open" mode where strangers connect; the
closest thing is a user sharing their QR code widely.

### 5.3 Grants

A Grant is `{ packageId, granteeUserId, rights, seq }` signed by the Package owner. Rights are `Read` or
`Write` (`Write` implies `Read`). A revocation is a Grant with `rights = None` and a higher `seq`. Grants
replicate like any other metadata; a serving node enforces the newest Grant it holds.

Consequence to state plainly: a reader can copy what it read. Grants control who the mesh *serves*, not what a
granted user does afterwards. Encryption at rest (§7.4) does not change that; it only lets nodes relay what
they may not read.

### 5.4 What is verified where

| Claim | Verified by | Trust needed in the peer |
|---|---|---|
| "This peer is device D of user U" | TLS client certificate pinned to the roster | none |
| "This object is X" | SHA-256 of the bytes | none |
| "This entry was written by U" | signature against U's roster | none |
| "This is the newest head" | `seq` and Lamport clock, newest wins | none for correctness; a peer can withhold, not forge |
| "U may read package P" | Grant signed by the owner | none |

Nothing a peer says is believed without one of these. A node that withholds data is a slow node, not a
security problem.

## 6. Transport

### 6.1 Discovery

Discovery is its own package, `Axiom.Mesh.Discovery`, usable without the rest. It answers one question:
*which addresses on this network claim to run a mesh node, and what device id do they claim?* The claim is
never trusted; the TLS handshake proves it.

| Mechanism | Where | Notes |
|---|---|---|
| mDNS / DNS-SD, service `_axiom-mesh._tcp` | default everywhere | The only mechanism that works on iOS without an entitlement fight. TXT record: `v=1`, `d=<deviceId>`. |
| UDP broadcast on port 41811 | desktop fallback | For networks that filter multicast. Same payload as the TXT record. |
| Static peers | all | Addresses the user typed. Tried on every start. |

A discovered node that the roster or the known-users list does not cover is ignored before any TCP
connection is made, so an unknown node costs nothing but a cache entry.

### 6.2 Connection

One TCP port per node, default 41810, served by LWSE Core: it binds the port, filters and admits connections,
applies the transport layer and hands each connection to the mesh entry protocol (§12). Admission limits
from LWSE (concurrent handshakes per port and per client, rate limits) apply unchanged; a hostile LAN is a
smaller version of a hostile internet.

Both directions use the same protocol and the same session handler. Whichever node connects first wins; if
two nodes dial each other at once, the connection from the lower device id survives and the other is closed
after `Hello`. See §15 for the open question of adopting outbound connections into LWSE Core.

### 6.3 Mutual TLS, pinned to the roster

Each device holds a self-signed X.509 certificate over its device key, valid for ten years, regenerated on
expiry. TLS 1.3 only, forward-secret AEAD suites only, ALPN `axiom-mesh/1`.

The transport layer is a `MeshTls` module stacked through `PortBuilder.Through`; LWSE's own TLS module stays
untouched (it does not require client certificates and validates against a CA, neither of which fits here).
Validation on both sides:

1. The peer certificate's public key must equal the key of a device in a roster of a known user (or in the
   own roster). Nothing else about the certificate matters: no CA, no name, no revocation list.
2. The roster entry must not be removed.
3. The device id in the certificate's subject must equal SHA-256 of the key.

A failing check closes the connection before any protocol byte. Pairing (§5.2) is the one flow that opens a
connection to a key not yet known; it runs on the same port through a separate ALPN `axiom-mesh-pair/1` with
its own short-lived handler and no access to the store.

## 7. Object store

### 7.1 Objects

Everything replicated is an **object**: a byte string named by its SHA-256. Three object types, told apart
by a one-byte tag at the start of the bytes:

| Type | Content | Size |
|---|---|---|
| `Chunk` | raw file bytes | ≤ 1 MiB |
| `Tree` | sorted list of `{name, mode, hash, size}`; `mode` is `File`, `Directory` or `Symlink`; a file's `hash` names a `ChunkList`, a directory's a `Tree` | small |
| `ChunkList` | the ordered chunk hashes of one file and its total size | small |
| `Entry` | a signed entry (§8.2) | small |

Files are cut into fixed 1 MiB chunks in the first version. Content-defined chunking (FastCDC) is a later
change to the chunker only: a `ChunkList` does not know how its boundaries were chosen.

Because every object is one frame or less on the wire (§12.1), an object is transferred whole or not at all,
and the receiver hashes it before it stores it. A node never stores bytes it did not verify.

### 7.2 On disk

Objects live as files under `objects/<xx>/<yy>/<hash>` in the node's data directory, written to a temp
name and renamed into place. The index (which objects exist, their size and type, when they were last used,
which pins hold them) lives in LDB. The blob bytes never go into LDB: LDB is a database, not a file system,
and a phone wants to evict a 2 GB attachment without rewriting a database page.

### 7.3 Pins and eviction

A pin is `{hash, reason}`. Reasons: `Head` (the entry is a head of a Package the node follows), `Policy`
(the retention policy of a Package wants it), `User` (the user said keep). An object with no pin is a
candidate; the evictor removes candidates least-recently-used first when the store exceeds its budget.

Default policies, overridable per Package and per device:

| Device class | Archive | Channel |
|---|---|---|
| Desktop | every version, every chunk | every message, every attachment |
| Phone | newest version's tree and chunks on demand | every message; attachments on demand, evictable |

"Offer the whole Package" is thus a policy a node opts into (desktops do by default), not a protocol rule.
A node always answers truthfully what it has (§12.3) and never promises more.

### 7.4 Encryption at rest (designed for, not built)

An object may later be stored and transferred as `Sealed { keyId, nonce, ciphertext }`, still named by the
SHA-256 of *those* bytes. Replication, pinning, have/want and eviction work on sealed objects unchanged;
only a service with the Package key can open them. The Package key would be wrapped to each grantee's user
key in the Grant. This is why the store addresses bytes, not plaintext, from day one.

## 8. Package model

### 8.1 A Package

```
Package
  id            fixed (§5.1)
  owner         userId
  kind          Archive | Channel | <service-defined>
  manifest      signed by owner: { id, owner, kind, name, created, policy, seq }
  grants        signed by owner (§5.3)
  entries       a DAG of signed Entry objects
  heads         one per writing device: the newest entry that device wrote
```

The manifest is replaceable (rename, change policy) with a higher `seq`. Kind never changes.

### 8.2 An Entry

```
Entry
  packageId
  deviceId        who wrote it
  clock           Lamport clock: max(clock of every parent) + 1
  parents         hashes of the entries this one follows (0 for the first, 1 normally, 2 for a merge)
  kind-specific payload
  signature       by the device key; valid only if the device is in the owner's roster or a writer's roster
```

Entry types the two first services define:

| Service | Kind | Entry | Payload |
|---|---|---|---|
| Sync | Archive | `Version` | `{ tree hash, label?, prerelease?, notes? }` |
| Chat | Channel | `Message` | `{ text, attachments: [{name, hash, size}], replyTo? }` |
| Chat | Channel | `Redaction` | `{ target entry hash }` — the author or owner withdraws a message; the bytes stay, the UI hides them |

Attachments are trees or chunk lists in the same store. "Send this folder in chat" and "publish this folder
as an Archive" reference the same objects; promoting one to the other is a new Entry, not a copy.

### 8.3 Order and concurrency

Every device may append while offline. The DAG absorbs that: a device appends to its own head; on sync a
device sees several heads.

- **Channel**: no merge. The visible order is `(clock, deviceId)`, which is total and the same on every
  device. Late-arriving messages slot in where their clock says.
- **Archive**: a device that holds two heads of the same branch creates a `Version` with both as parents.
  Paths changed on only one side take that side. A path changed on both sides keeps the side with the higher
  `(clock, deviceId)` and writes the other as `name.conflict-<deviceId>.<ext>` next to it. No content merge.
  The user resolves by deleting one. This is Syncthing's behaviour and it has survived a decade of use.
- **Branches** are a label on an Archive head: `main` by default, `beta` or whatever the user names. A
  branch forks by writing a `Version` whose parent is on another branch and whose label is new. Branch labels
  live in the manifest's `branches: { name → head hash }` so that a fork is a signed owner action.

### 8.4 Who may write

The owner's devices always. A user with a `Write` grant: their devices too. The signature check in §8.2
is the enforcement; a node drops an entry whose signer has no right, and a serving node does not forward it.

## 9. Replication

The protocol (§12) is the same for every kind. A node:

1. On connect, exchanges `Hello` and then `Packages`: every Package the sender holds **that the receiver's
   user is granted**. Nothing else is listed; a Package you may not read does not exist on the wire.
2. For every Package both follow, exchanges heads. A head the receiver lacks is wanted.
3. Walks from a new head down the parents it lacks, wanting each Entry, then the Entry's payload objects as
   the retention policy says (a desktop wants the whole tree and all chunks; a phone wants the tree and
   stops).
4. Pushes `HeadsChanged` to every connected peer the moment a local write lands. Chat latency is network
   latency; nobody polls.

Wants are answered from whichever connected peer said it has the object (`Have`), spread across peers by
remaining capacity. A node that has nothing of a Package yet gets the entries from whoever has them and the
bytes from whoever has those; no single peer needs to hold everything.

## 10. Architecture

### 10.1 Layers

Honest layers at the bottom, one dishonest runtime at the top, in the ProtoStream tradition:

| Layer | Honest? | Contents |
|---|---|---|
| Model | yes | hashing, object encoding, Entry and manifest signing and verification, DAG walk, order, merge rules |
| Protocol | yes | the ProtoStream description of `axiom-mesh/1`: messages, state machine, limits |
| Store | mostly | the content-addressed store: pure given a storage environment, which the test suite provides in memory |
| Discovery | no | mDNS, UDP, static |
| Node | no | LWSE server, MeshTls, peer table, replication driver, pin evictor, the management API |
| Services | mixed | Sync and Chat: honest rules (what a Version means, how to order messages) on top, watchers and UI adapters below |

Tests for the honest layers need no sockets, no disk and no clock: ProtoStream's `CodecHarness` and
`DefinitionAssert` for the protocol, in-memory LDB for the store, hand-built DAGs for the model.

### 10.2 Core is an LWSE application

Core is the base: identity and keys, roster, discovery, the mesh port with `MeshTls`, the entry protocol,
admission, the peer table, the Package registry, grants, the object store and pins, and the management API.
Core knows that a Package has entries with parents, clocks and signatures. Core does not know what an entry
means.

Core is an LWSE server with:

- one port (41810) stacked `Through(MeshTls)` with the mesh protocol as its entry protocol;
- one loopback port (41812) with `Axiom.LWSE.Http` for the management API and the web UI;
- Core's own modules offering the sockets services fill.

### 10.3 Services are LWSE modules

A service is a module that:

- registers a **kind** and the entry payload codecs for it (a contribution to Core's `PackageKind` socket);
- contributes rules Core calls: validate a payload, pick the objects a payload references (so replication
  and pinning can follow them), merge two heads (Archive) or not (Channel);
- reads Core's exposures: the store, the Package registry, peer events, head-changed events;
- optionally contributes endpoints to the management API and a sub-protocol for live traffic between two
  instances of the same service (§12.5), for things that are not entries, such as typing indicators or a
  transfer progress stream.

LWSE's module lifecycle (`Register`, `Build`, `Link`, start, stop, `Release`) is the service lifecycle;
there is no second one. Refusals at build stop the node before a port is bound, so a misconfigured service
is a startup error with a message, never a runtime surprise.

### 10.4 Management and the local API

`Axiom.Mesh.Host` is the daemon: Core plus the first-party services, configured from one file, running as a
Windows service, a systemd unit or an Android foreground service. On iOS the host runs inside the app while
the app is open.

The loopback HTTP port is the one way in for anything not compiled into the host: the Avalonia app, a CLI,
a script, a third-party local tool. It is authenticated by a token in a file only the user can read. It
exposes: identity and pairing, devices, known users, Packages and grants, heads and history, pins and
policies, peers and transfers, and per-service endpoints. The web UI is a static front-end on the same port.
`Axiom.LWSE.ApiDocs` documents it for free.

### 10.5 Apps

`Axiom.Mesh.App` is one Avalonia code base for Windows, Linux, macOS, Android and iOS. On desktop it talks to
the host daemon over the loopback API, so closing the window keeps the node serving. On mobile it embeds the
host. The app is a client of the API only: no app code touches the store, the wire or a key.

## 11. Repository layout and packages

```
Axiom.Mesh.Discovery/        mDNS, UDP broadcast, static peers; no dependency on the rest
Axiom.Mesh.Model/            objects, entries, signing, DAG, order and merge rules (honest)
Axiom.Mesh.Protocol/         the ProtoStream description of axiom-mesh/1 and its messages (honest)
Axiom.Mesh.Core/             store, registry, roster, grants, MeshTls, replication, pins, Core modules
Axiom.Mesh.Host/             the daemon: configuration, management API, web UI, platform services
Services/Axiom.Mesh.Sync/    the Archive kind and the folder watcher
Services/Axiom.Mesh.Chat/    the Channel kind
Apps/Axiom.Mesh.App/         Avalonia, all platforms
Apps/Axiom.Mesh.Cli/         a command line over the local API
Axiom.Mesh.Testing/          in-memory mesh of N nodes, fake clock, fake discovery
UnitTests/                   one test project per project, same layout
Documentation/, Samples/, Benchmarks/
```

Dependencies point downwards only: Apps → Host → Services → Core → Protocol, Model → (ProtoStream, LDB).
Discovery is a leaf Core uses. A convention test enforces the direction.

## 12. Wire protocol `axiom-mesh/1`

### 12.1 Framing and limits

Length-prefixed frames, `UInt32` big-endian, maximum frame 1 MiB + 4 KiB, so one `Chunk` with its header
fits one frame. Message id as a `UInt16` after the length. Idle timeout 5 minutes with a ProtoStream
heartbeat at 60 s. Per-message read deadline 30 s. Minimum data rate against slow peers as ProtoStream
defaults. Every list on the wire has a stated maximum; a frame over any limit closes the connection.

### 12.2 Session states

```
Start ──Hello/Hello──► Open ──ServiceOpen──► (switched to a service protocol) ──close──► Open
```

In `Open` every message of §12.3 may flow in either direction at any time; the protocol is symmetric. There
is no request/response pairing at the protocol level except `Ping`/`Pong`; correlation is by object hash and
Package id, which lets a node answer wants out of order and from several peers.

### 12.3 Messages

| Id | Message | Fields | Meaning |
|---|---|---|---|
| 1 | `Hello` | protocol version, device id, user id, roster seq, capabilities, services offered | First message both ways. A mismatch in major version closes. |
| 2 | `Roster` | a signed roster | Sent when the peer's `Hello` names an older roster seq, and whenever the own roster changes. |
| 3 | `Packages` | list of `{id, kind, manifest seq, heads: [{deviceId, hash, clock}]}` | Everything the receiver is granted. Sent after `Hello` and on change. Max 4096 per message, paged. |
| 4 | `Manifest` | a signed manifest | On request or change. |
| 5 | `Grant` | a signed grant | Sent to the grantee's devices and to any node that serves the Package. |
| 6 | `HeadsChanged` | package id, heads | Push after a local write. |
| 7 | `Have` | list of hashes (≤ 1024) | "I hold these." Sent in answer to `Want` for the subset held, and unsolicited after new objects arrive for a Package the peer follows. |
| 8 | `Want` | list of hashes (≤ 1024) | "Send these if you have them." |
| 9 | `Object` | hash, bytes | One object. The receiver verifies the hash before storing. |
| 10 | `NotFound` | hash | Answer to a `Want` the sender cannot satisfy. |
| 11 | `Cancel` | list of hashes | The sender no longer wants these; a queued `Object` may be dropped. |
| 12 | `Ping` / 13 `Pong` | nonce | Framework-answered via ProtoStream `Respond`. |
| 14 | `ServiceOpen` | service id, version | Switches the session to that service's protocol (§12.5). Refused with `ServiceRefused` if not offered. |
| 15 | `ServiceRefused` | service id, reason | |
| 16 | `Bye` | reason | Orderly close. |

Every hash is 32 bytes; every id (user, device, Package) is 32 bytes; every signature is DER-encoded ECDSA
P-256. Strings are UTF-8 with a `UInt16` length prefix and a stated maximum.

### 12.4 Flow control

A node keeps at most 64 outstanding `Want`s per peer and at most 8 MiB of `Object` bytes queued to write per
peer; past that it stops reading wants from that peer until the queue drains. ProtoStream's write lock and
the pipe's back-pressure do the rest. There is no windowing protocol on top of TCP.

### 12.5 Service sub-protocols

`ServiceOpen` hands the connection to a protocol the service describes with ProtoStream, through LWSE's
protocol switching, so admission, observation and drain still see it. A service protocol is for traffic
that is not an entry: live progress, typing indicators, a screen or a game later. It ends with the
service's own close message and the session returns to `Open`. Each service version is its own protocol
name (`axiom-mesh-chat/1`); a peer that offers a different version is simply not switched to.

## 13. Storage (LDB)

One LDB database per node under the data directory, opened by the host; the app reads it only through the
API. Tables, as `[LdbEntity]` classes:

| Table | Key | Holds |
|---|---|---|
| `Users` | user id | public key, display name, known since, last roster seq |
| `Devices` | device id | user id, public key, name, removed flag |
| `Packages` | package id | owner, kind, manifest bytes, manifest seq, local policy |
| `Grants` | (package id, grantee) | rights, seq, signature |
| `Heads` | (package id, device id) | entry hash, clock |
| `Entries` | entry hash | package id, device id, clock, parents, payload type — the index; the bytes are an object |
| `Objects` | hash | type, size, stored at, last used |
| `Pins` | (hash, reason, source) | |
| `Peers` | device id | last addresses, last seen, last roster seq, statistics |
| `ChatIndex` | (package id, clock, device id) | entry hash, author, preview — Chat's own table, owned by the Chat service |

Rules: a table belongs to exactly one module; another module reads it through that module's exposure, never
directly. Migrations use `Axiom.LDB.Migration` and ship with the version that needs them.

## 14. Security considerations

Threat model: the LAN is hostile (other people's devices, an open Wi-Fi). Paired users are trusted to the
extent of their grants and no further. A device of your own that is lost is removed from the roster.

| Threat | Answer |
|---|---|
| Impersonating a device | mutual TLS pinned to roster keys; nothing else is accepted |
| Man-in-the-middle at pairing | both users compare the short authentication string |
| Forged entries or objects | signatures against rosters; content addresses; nothing unverified is stored or forwarded |
| Serving an old state | heads carry clocks and rosters and grants carry `seq`; newest wins, a peer can delay but not roll back |
| Reading a Package without a grant | not listed, not served; §7.4 later makes relayed bytes unreadable too |
| A lost phone | remove the device; its key is refused at every handshake from then on; rotate the user key if the phone held it (it should not: only the first device holds the user key unless the user exports it) |
| Resource exhaustion | LWSE admission and ProtoStream limits: handshakes per client, frame sizes, wants outstanding, queue bytes, idle and read deadlines |
| Discovery spam | discovery output is only a hint; an unknown id is never dialled |
| A malicious service module | none; a module is compiled in. Third-party code lives behind the local API and its token |

Not defended: traffic analysis on the LAN (who talks to whom and how much), and a granted user leaking what
they read.

## 15. Open questions

1. **Outbound connections in LWSE.** LWSE Core admits inbound connections. A mesh node also dials. ProtoStream's
   `Connection.FromStream` covers the wire, but admission, observers and drain should see outbound sessions
   too. Check whether Core can adopt an externally opened stream; if not, that is a Core socket to propose
   in LWSE, not something to work around in Mesh.
2. **LWSE and LDB on Android and iOS.** Both are plain .NET 8/10 libraries without host dependencies and
   should run under Avalonia on both platforms, but neither has been run there. Spike before the App exists:
   a node on a phone that pairs with a desktop and syncs one Channel.
3. **Ed25519.** Preferable to ECDSA for signatures; waits for the BCL or a decision to take a dependency.
4. **Encryption at rest** (§7.4): whether the first release should already seal objects. Current answer: no,
   but the store addresses bytes so that it can.
5. **Background execution on phones.** Android: a foreground service with a persistent notification. iOS:
   none; sync when open, with a local notification on new messages only while in the foreground. Decide
   what the Chat service promises on iOS.

## 16. Phases

| Phase | Delivers | Done when |
|---|---|---|
| 0 | spikes for open questions 1 and 2 | both answered in writing in this document |
| 1 | Discovery, Model, Protocol, Core, Host with the CLI; Sync service; desktop only | two PCs on one LAN pair, share an Archive, sync a folder both ways, survive one being offline, and the honest layers are tested without sockets or disk |
| 2 | Chat service, management web UI | a Channel between two users with attachments, pushed, ordered the same on both ends |
| 3 | Avalonia desktop app | the UI does everything the CLI does |
| 4 | Android, then iOS, with the phone retention policy | a phone pairs by QR code, chats and opens the newest version of an Archive |
| later | encryption at rest, FastCDC, more services | |
