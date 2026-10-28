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
  assemblies loaded at runtime: every service speaks the local protocol (§10.3); first-party ones are hosted
  in-process, any other runs as its own program on the pipe.
- Built on the Axiom family: [ProtoStream](https://github.com/DeadMoon0/Axiom.ProtoStream) owns the wire,
  [LWSE](https://github.com/DeadMoon0/LWSE) is the host (ports, transports, admission, modules),
  [LDB](https://github.com/DeadMoon0/LDB) holds state. No other runtime dependency.
- Cryptography from `System.Security.Cryptography` only: SHA-256 for content addresses, ECDSA P-256 for
  signatures and for the TLS certificates, TLS 1.3 via `SslStream`. One key type serves both signing and
  transport, and all of it runs on Windows, Linux, macOS, Android and iOS without a native library. (Ed25519
  would be the nicer signature scheme; it is not in the BCL and would cost a dependency. Revisit when it is.)
- Local network first. Nodes find each other only on their own network. Two networks meet only through a
  configured **Edge** (§16), and only what the Edge's owner listed crosses. No NAT traversal, no hole punching,
  no central rendezvous: an Edge either has a reachable address or both Edges dial a relay the user runs.
- The public API follows the Axiom conventions: no optional parameters, no positional records without
  `[ClosedShape]`, no bare numbers, honest layers below one dishonest runtime (see §10).

## 3. Context and scope

In scope: discovery on the LAN, pairing between users, authenticated encrypted transport, the object store,
the Package model, replication, retention, the two first services (Sync, Chat), the local protocol over the
pipe, and the app for desktop and mobile with its service views and mesh analytics.

Out of scope for the first releases: Edges between two meshes (designed in §16, not built), encryption of
Package content at rest (designed for, not built; §7.4), three-way content merges, multi-parent history beyond merge entries, running as a system
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
| **Mesh** | The nodes that discovery reaches: one network. A mesh has no identity and no configuration; it is where you are. |
| **Edge** | A node that also holds a Link to an Edge in another mesh and carries listed Packages across it. |
| **Link** | A peer connection between two Edges over a transport that is not the LAN: a direct address or a relay. |
| **Crossing list** | The Edge owner's explicit list of what may travel over a Link, per direction. Empty by default. |
| **Relay** | A server the user runs somewhere both Edges can reach, which joins their two outbound connections and sees only ciphertext. |

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
  device raises `seq`; the user does it from the device that holds the user key, through the same short
  exchange as pairing, in device mode (LOCAL-SPEC.md §6.2). A peer accepts a roster only with a higher `seq` than the one it holds. Removing a
  device is a new roster without it; peers stop talking to that device as soon as the roster reaches them.
- **Package id** = SHA-256 of `{ ownerUserId, created nonce }`, fixed at creation. The nonce and the owner's
  public key travel in the Manifest, so every receiver checks the derivation and nobody but the owner's key
  can produce a Manifest for an id. Names are metadata and may change; ids never do.

A user's own devices therefore trust each other through the roster, not through pairwise pairing. Pairing is
only between **users**.

### 5.2 Pairing two users

1. Alice shows a QR code (or reads out a six-word code) containing her user public key and the address of the
   device she is on.
2. Bob's device connects, both sides see a short authentication string derived from both public keys, and both
   confirm it matches on screen. A hostile LAN cannot substitute a key without the strings differing.
3. Both devices store the other user's public key and current roster as **known**; the roster must travel
   with the key, because without it no device of the other user would pass the TLS check. Later rosters
   then flow over any connection, so every device of Alice knows every device of Bob.

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
| "This entry was written by U" | signature against U's roster; U's key reaches the node in the Manifest, Grant or Roster, so a reader need not have paired with every writer | none |
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
| Links | Edges only | The other Edge, by its configured transport (§16.2). Not discovery in the strict sense: the address is configuration. |

Devices in another mesh are never discovered and never dialled. They are reached only in the sense that the
local Edge holds what they published (§16).

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

A failing check closes the connection before any protocol byte on the *accepting* side. The dialling side is
different: TLS 1.3 lets a client finish its handshake before the server has judged the client certificate, so
a refused dialler sees its session open and then close, never a handshake error. The protocol therefore
treats a session as established only once the peer's `Hello` arrived; a close before that is counted and
rate-limited as a refusal, and nothing is sent before `Hello` except `Hello`. Implementation note for Windows:
its TLS stack cannot use a key that lives only in memory, so a freshly created device certificate goes
through a PKCS#12 round trip before it is used (verified in `Spikes/OutboundAdoption`).

Pairing (§5.2) is the one flow that opens a connection to a key not yet known; it runs on the same port through a separate ALPN `axiom-mesh-pair/1` with
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

A pin is `{hash, reason, source}`. Five reasons: `Head` (the entry is a head of a Package the node follows, or
an ancestor needed to verify one), `Policy` (the retention policy of a Package wants it), `User` (the user
said keep), `Service` (a service put the object or asked to keep it; LOCAL-SPEC.md §9.6) and `Outbound` (an
entry written on this device references it and no peer has fetched it yet, so that a phone cannot evict the
only copy of what it just published; LOCAL-SPEC.md §9.4, SPEC.md §11.2). An object with no pin is a candidate;
the evictor removes candidates least-recently-used first when the store exceeds its budget.

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
  branch forks by writing a `Version` whose parent is on another branch and whose label is new; any writer's
  service can do that. The manifest's `branches: { name → head hash }` is the owner's display declaration of
  a head per branch (SPEC.md §9.5), set through `UpdatePackage` (LOCAL-SPEC.md §6.4); it does not gate forks
  and does not override the order.

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
4. Pushes a change notice to every connected peer that may see the Package the moment its listable state
   changes, from a local write or from an object it received, except back to the peer it came from. A change
   therefore crosses any number of hops at network latency; nobody polls.

Wants are answered from whichever connected peer said it has the object (`Have`), spread across peers by
remaining capacity. A node that has nothing of a Package yet gets the entries from whoever has them and the
bytes from whoever has those; no single peer needs to hold everything.

## 10. Architecture

### 10.1 Layers

Honest layers at the bottom, one dishonest runtime at the top, in the ProtoStream tradition:

| Layer | Honest? | Contents |
|---|---|---|
| Model | yes | hashing, object encoding, Entry and manifest signing and verification, DAG walk, order, merge rules |
| Protocol | yes | the ProtoStream descriptions of `axiom-mesh/1` (peers) and `axiom-mesh-local/1` (the pipe): messages, state machines, limits |
| Store | mostly | the content-addressed store: pure given a storage environment, which the test suite provides in memory |
| Discovery | no | mDNS, UDP, static |
| Host | no | LWSE server, MeshTls, the pipe, peer table, replication driver, pin evictor, service supervision |
| Services | mixed | Sync and Chat: honest rules (what a Version means, how to order messages) on top, watchers and pipe clients below |

Tests for the honest layers need no sockets, no disk and no clock: ProtoStream's `CodecHarness` and
`DefinitionAssert` for the protocol, in-memory LDB for the store, hand-built DAGs for the model.

### 10.2 The Host owns everything

`Axiom.Mesh.Host` is one process, the daemon, and the only process that holds anything: the keys, the
rosters, discovery, every mesh connection, the object store, the database, pins and replication. Nothing
else touches any of it. Every other program on the device, the app and the CLI included, is a client of the
Host over the **pipe** (§10.4). This is the Docker model: one engine, one socket, many clients.

The Host knows that a Package has entries with parents, clocks and signatures. It does not know what an
entry means; that is a service's business (§10.3).

The Host is one LWSE server with three ports, all served through Core's one serve path:

- the mesh port (41810), a bound port stacked `Through(MeshTls)` in server role with `axiom-mesh/1` as its
  entry protocol;
- the outbound port, an adopt-only port with `MeshTls` in client role, the same entry protocol: every peer
  the Host dials is reserved with `TryReserve` and served with `AdoptAsync` (§15.1);
- the local port, an adopt-only port with `axiom-mesh-local/1` as its entry protocol and no TLS: every pipe
  connection the Host accepts is adopted into it, and every in-process service connects to it through an
  in-memory stream pair adopted the same way.

Admission, observers, retire and drain therefore cover peers, dialled peers, pipe clients and in-process
services alike, in one table.

### 10.3 Services speak the local protocol

A **service** defines a Package kind and what its entries mean: Sync defines `Archive` and `Version`, Chat
defines `Channel`, `Message` and `Redaction`. A service is written against one contract only: the local
protocol in its **service role** (§10.4). Through it a service

- registers its kind and the entry kinds it owns, within what the Host's configuration lets that service
  register; the Host parses the built-in payload layouts (`Version`, `Message`, `Redaction`) itself and
  stores extension kinds opaquely with the references the writer named, so no service validates payloads for
  the Host (LOCAL-SPEC.md §9.4);
- receives every entry of its kind the Host stores, and every listing, head, manifest and grant change of its
  Packages, as pushed events;
- writes new entries, which the Host signs with the device key, stores and replicates;
- asks the Host to pin or release objects, and to materialise or read them;
- publishes what the user-facing clients may ask it (a Channel's rendered timeline, a folder's sync state),
  which the Host relays to user clients as that service's namespace of the local protocol.

Where a service runs is a deployment choice, not a code choice. In the first release Sync and Chat are
hosted **in-process**: compiled into the Host, connected to the local port over an in-memory stream pair,
started and stopped by the Host's LWSE service lifecycle. The same binary contract lets a service run
**out-of-process** later, as its own program connecting to the pipe, in any language, with crash containment
and separate updates; the Host then supervises it. Native AOT rules out loading assemblies at run time, so
there is no third option, and no service ever links against the Host's internals.

Bulk data does not cross the pipe twice. Verified objects are immutable files under their hash (§11), so a
service may read them directly from the store directory, read-only; the pipe carries control, entries and
new objects to be verified and stored. Writes go through the Host, always.

### 10.4 The pipe and the local protocol `axiom-mesh-local/1`

The pipe is a named pipe on Windows (`\\.\pipe\axiom-mesh`), a Unix domain socket on Linux
(`$XDG_RUNTIME_DIR/axiom-mesh.sock`) and macOS (`$TMPDIR/axiom-mesh.sock`), and an in-memory pipe inside the
app process on Android and iOS. Its security is the operating system's: the pipe ACL or the socket file's
mode admits the same OS user and nobody else, and the Host checks the peer's process identity (user, process
id, executable) behind that and refuses any other user; no password, no port, nothing reachable from the
network. A process of the same user is that user, as with ssh-agent and Docker Desktop. The requests that
spend the user key (confirm a pairing, grant, revoke, remove a device, forget a user) are logged with the
process that sent them and may be confined by configuration to named executables; an out-of-process service
the Host spawns proves it with a token the Host put into its environment (LOCAL-SPEC.md §2.2, §4.2).

The local protocol is described with ProtoStream like the mesh protocol: typed binary messages, requests
and responses paired by a correlation id, and subscriptions through which the Host pushes events (a peer
came up, a message arrived, a sync progressed) so no client polls. It has two roles, stated in its `Hello`
and granted by the Host's configuration:

| Role | Who | May |
|---|---|---|
| **user** | the app, the CLI, a script, any local tool | everything the user may do: identity and pairing, devices, known users, Packages and grants, history, pins and policies, peers and transfers, telemetry (§10.5), and each service's namespace |
| **service** | Sync, Chat, a future service | §10.3; a name the Host's configuration lists as a service; in-process services are trusted by construction |

Nothing finer than these two roles exists; finer rights belong to the operating system. A JSON-over-HTTP
gateway for scripts and curl can be added later on a second pipe name as a separate module; it is not part
of the first release. The local protocol has its own specification beside [SPEC.md](SPEC.md) (AMP/1):
[LOCAL-SPEC.md](LOCAL-SPEC.md), with the same discipline, because once third-party tools speak it, it is harder
to change than the mesh protocol. Where this section and LOCAL-SPEC.md differ, LOCAL-SPEC.md wins; its §1.6
lists the decisions it made where this section was open.

### 10.5 Platforms and the app

| Platform | The Host runs as | The pipe |
|---|---|---|
| Windows | a Windows service, or a per-user autostart process | `\\.\pipe\axiom-mesh` |
| Linux | a systemd user unit | `$XDG_RUNTIME_DIR/axiom-mesh.sock` |
| macOS | a launchd agent | `$TMPDIR/axiom-mesh.sock` |
| Android | a foreground service inside the app process | in-memory, same process |
| iOS | inside the app, while the app is open | in-memory, same process |

`Axiom.Mesh.App` is one Avalonia code base for all five. It is a user client of the local protocol and
nothing else: no app code touches the store, the wire or a key, and on desktop closing the window keeps the
node serving. The in-process pipe on mobile means the app speaks exactly the protocol the desktop app
speaks; only the transport differs.

**Service views.** The app is not a thin management console: each service gets a full user interface of its
own inside the app, as rich as the service needs (a chat timeline with attachments and replies, a folder
view with per-file sync state and conflicts). A service view talks only to its service's namespace of the
local protocol, never to the service itself, so the view does not care whether the service runs in-process
or out-of-process. First-party service views are compiled into the app; how a third-party service brings a
view (a separate app of its own, or a declarative view description over the protocol) is decided when the
first one exists. Specifics of the UI come later; the rule that holds now is that every pixel the app draws
comes from the local protocol, so a CLI could show the same facts.

**Mesh analytics.** The app shows what the mesh is doing, from a telemetry namespace of the local protocol:
peers and their sessions (direction, TLS suite, age, last seen, device and user), bytes and objects per peer,
Package and direction, replication state per Package (heads known against heads held, objects wanted
against held), store size against budget and what pins hold it, admission refusals and violations by peer,
discovery events, and the Edge crossing log (§16.4). The Host keeps these as time series in its database
(§13) at one-minute resolution for seven days and as totals for ever, and pushes live updates over a
subscription; the app renders history and live state from the same feed. Nothing leaves the device.

## 11. Repository layout and packages

```
Axiom.Mesh.Discovery/        mDNS, UDP broadcast, static peers; no dependency on the rest
Axiom.Mesh.Model/            objects, entries, signing, DAG, order and merge rules (honest)
Axiom.Mesh.Protocol/         the ProtoStream description of axiom-mesh/1 and its messages (honest)
Axiom.Mesh.Local/            the ProtoStream description of axiom-mesh-local/1, both roles (honest);
                             the one package a client or a service references
Axiom.Mesh.Core/             store, registry, roster, grants, MeshTls, replication, pins, the local port
Axiom.Mesh.Host/             the daemon: configuration, the pipe per platform, service supervision
Services/Axiom.Mesh.Sync/    the Archive kind and the folder watcher; a client of Axiom.Mesh.Local
Services/Axiom.Mesh.Chat/    the Channel kind; a client of Axiom.Mesh.Local
Axiom.Mesh.Edge/             the Edge role: links, transports, crossing lists, the crossing log (later)
Apps/Axiom.Mesh.App/         Avalonia, all platforms; a user client of Axiom.Mesh.Local
Apps/Axiom.Mesh.Cli/         a command line; a user client of Axiom.Mesh.Local
Apps/Axiom.Mesh.Relay/       the relay server: joins two Edges' outbound connections (later)
Axiom.Mesh.Testing/          in-memory mesh of N nodes, fake clock, fake discovery, an in-memory pipe
UnitTests/                   one test project per project, same layout
Documentation/, Samples/, Benchmarks/
```

Dependencies point downwards only: Host → Core → Protocol, Model → (ProtoStream, LDB); Services and Apps →
Local → (ProtoStream) and nothing of Core or Host. Discovery is a leaf Core uses. The Host references the
services only to start them in-process; a convention test enforces that no service and no app references
Core, Host or Model.

## 12. Wire protocol `axiom-mesh/1`

> The normative definition of the wire protocol, the object formats, signing, verification, replication and
> local data handling is [SPEC.md](SPEC.md) (AMP/1). This section is the sketch the spec grew from and is kept
> for the reasoning; where the two differ, the spec wins. Known differences: the spec folds `Roster`,
> `Manifest` and `Grant` into objects fetched by `Want`, replaces `HeadsChanged` with `PackageChanged`, adds
> `RosterQuery`, `RosterNotFound` and `ServiceAccepted`, raises the frame limit to 4 MiB + 4 KiB so a large
> `Tree` fits, encodes signatures as 64-byte low-s `r ‖ s` rather than DER, carries user keys and the Package
> nonce inside Manifests, Grants and Rosters, and caps `Want` at 64 hashes and `Packages` at 64 per page.

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
| `Meta` | key | the store epoch (a random id created with the database, replaced on restore or reset, so clients that keep their own index know store sequences started over; LOCAL-SPEC.md §9.3), the schema version |
| `Entries` | entry hash | package id, device id, clock, parents, payload type, `storeSeq` (unique, increasing, the order entries were stored, for service catch-up; LOCAL-SPEC.md §5.6), `redacted` (a stored Redaction names it; LOCAL-SPEC.md §5.6) — the index; the bytes are an object |
| `Objects` | hash | type, size, stored at, last used |
| `Pins` | (hash, reason, source) | |
| `Peers` | device id | last addresses, last seen, last roster seq, statistics, `via link` when reached over a Link |
| `Links` | link id | the other Edge's device id, transport, state, crossing lists per direction — the Edge module's table (§16) |
| `Crossings` | (link id, package id, direction, day) | objects and bytes that crossed — the audit the management UI shows |
| `ChatIndex` | (package id, clock, device id) | entry hash, author, preview — Chat's own table, owned by the Chat service |
| `Telemetry` | (series, key, minute) | counters and gauges for the analytics of §10.5: per peer, Package and direction; one-minute resolution for seven days, then rolled into `TelemetryTotals` |
| `TelemetryTotals` | (series, key) | lifetime totals |

Rules: a table belongs to exactly one module; another module reads it through that module's exposure, never
directly. Migrations use `Axiom.LDB.Migration` and ship with the version that needs them. Exactly one process
opens the database: the host. On Windows, Linux and Android LDB would allow more through byte-range locks; on
macOS, iOS and tvOS it has none and admits one process per directory. The app never opens it (§10.5), so the
rule costs nothing and the behaviour is the same on every platform.

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
| Data leaving the mesh through an Edge | nothing crosses a Link unless it is on that direction's crossing list; the Edge is a party to every message it carries and can check each one; every crossing is logged |
| An Edge's port reachable from the internet (direct Link) | mutual TLS pinned to rosters before any byte, LWSE admission; the relayed transport opens no inbound port at all and is the default |
| A hostile or compromised relay | it joins two TLS connections it cannot read; it can delay, drop or count, not read or forge |
| A compromised Edge | it is a granted device and can leak what it may read, like any granted device; it cannot widen its own crossing list without the owner's signature, and removal from the roster cuts the Link |
| A malicious local program | the pipe admits the same OS user only, checked by ACL and by process identity, and that user may do everything anyway; the service role is granted by the Host's configuration by name and bound to kinds, an in-process name is never granted on the pipe and a spawned service proves itself with a token, so a stray program does not register a kind by accident; the user-key requests are logged with their process and may be confined to named executables (LOCAL-SPEC.md §2.2, §13) |

Not defended: traffic analysis on the LAN (who talks to whom and how much), and a granted user leaking what
they read.

## 15. Open questions

1. **Outbound connections in LWSE.** *Answered by `Spikes/OutboundAdoption` on 2026-10-28, against LWSE 0.2.0.*
   Core's internal serve path takes any stream, however it arrived, and the spike proves it end to end: a
   dialled socket pumped into an in-memory port got admission (a second dial to the same peer was refused by
   `PerClient`), the pinned TLS layer in client role (TLS 1.3, mutual, ALPN), the protocol handler, every
   observer event (accepted, session opened, retired by drain, closed) and drain. Eight checks pass.
   What 0.2.0 lacks is the public API: `ConnectInMemory` exists only on a server described `InMemory()`, which
   binds no socket at all, so a node needs *two* servers (one bound, one in memory) and a byte pump between the
   socket and the in-memory port. That workaround is not production: two admission tables and two drains per
   node, an extra copy per direction, admission decided after the TCP connect instead of before it, and the
   TLS role fixed per port. **Decision:** Mesh does not ship the workaround. LWSE got the API instead, on branch
   `claude/adopt-connections-f8d0e2` (commit `7092996` on top of `v2`, the 0.3.0 line; not merged or released
   yet): a port is listened on with an `AdoptOnlyPort` handle (`.Listen(Ports.Outbound, p => ...)`) and binds
   nothing; `LWSEServer.TryReserve(port, remote, timeout, out AdmissionLease)` decides deny list, filters and
   admission *before* the dial and holds the place as a lease; `LWSEServer.AdoptAsync(lease, stream, local)`
   serves the stream through the one serve path and completes when the connection ended; refusals are typed
   `AdoptionRefusedException`s; modules reach the same seam through `ModuleBuildContext.Adoption`
   (`IConnectionAdoption`). Adopted and accepted connections share one admission table, one observer list and
   one drain. The TLS role stays a port option; a node has one inbound and one adopt-only port on one server.
   Phase 1 starts once that branch is merged and released.
   Two findings for the protocol, now in §6.3: TLS 1.3 lets the dialling side finish its handshake before the
   server judged its certificate, so a refused dialler sees its session open and then close; and Windows'
   TLS stack needs the device key in a persisted form, which a PKCS#12 round trip gives.
2. **LWSE and LDB on Android and iOS.** *Android answered on 2026-10-28; iOS open.* LWSE Core, ProtoStream
   and LDB (Core, Storage, Entity with its source generator) build for `net10.0-android` with full trimming
   and the AOT profile without one trimmer warning, and `Spikes/Android` passes every check on an Android 15
   emulator (x86_64, API 35): the same adoption scenario as on Windows, with mutual TLS 1.3 through Android's
   own TLS stack (client certificates included, suite `TLS_AES_128_GCM_SHA256` where Windows picked
   `TLS_AES_256_GCM_SHA384`), admission, observers and drain, and an LDB file database in the app's files
   directory written and read back. Nothing platform-specific was needed beyond the `INTERNET` permission.
   Still to do: the same on iOS, which needs a Mac. One LDB fact is already settled and now in §13: on macOS,
   iOS and tvOS LDB has no byte-range locks and allows one process per database directory, which is the Mesh
   model anyway (§10.4).
3. **Ed25519.** Preferable to ECDSA for signatures; waits for the BCL or a decision to take a dependency.
4. **Encryption at rest** (§7.4): whether the first release should already seal objects. Current answer: no,
   but the store addresses bytes so that it can.
5. **Background execution on phones.** Android: a foreground service with a persistent notification. iOS:
   none; sync when open, with a local notification on new messages only while in the foreground. Decide
   what the Chat service promises on iOS.
6. **Live service traffic across a Link** (§16.6): whether and how a service's sub-protocol is relayed by an
   Edge at message level. Not needed for Sync and Chat; decide when a service needs it.
7. **Carrying without reading.** An Edge can only carry a Package it is granted. Sealed objects (§7.4) would
   let it carry a Package on the crossing list without a grant. That makes §7.4 more valuable than it looked.

## 16. Edges: connecting two meshes (future)

Two meshes, say home and office, sometimes need to exchange data. The rule that decides the whole design:
**what leaves a mesh is listed by its owner, item by item, and nothing else leaves.** That rules out any
form of opaque tunnel, because a tunnel cannot be checked against a list. An Edge is therefore not a
proxy: it is an ordinary node that is a peer in both meshes and copies, at the Package level, exactly what
its crossing list names.

### 16.1 What an Edge is

An Edge is a normal node (usually a desktop or a small always-on machine) with one addition: a **Link** to
an Edge in another mesh. Over the Link the two Edges talk the ordinary mesh protocol (§12) as two peers; the
only differences are the transport underneath and the crossing list on top. No new identity is involved: the
two Edges are devices of users who are paired (or of the same user), authenticated by the same mutual TLS
against the same rosters as on the LAN.

Nothing in either mesh changes. A node at home sees its Edge as one more peer that happens to hold Packages
from the office. A node at the office never sees, dials or learns about a node at home. Chat from home to
office takes two extra hops: node to Edge, Edge to Edge, Edge to node. Pushed `HeadsChanged` keeps that at
network latency.

### 16.2 Link transports

| Transport | How | When |
|---|---|---|
| **Direct** | Edge A dials Edge B at a configured address and port, mutual TLS as on the LAN. | B has a reachable address (port forward, VPN, public IP). The only transport that opens an inbound port. |
| **Relayed** | Both Edges dial a Relay the user runs; each sends `Join { linkId }` after its TLS handshake with the Relay; the Relay joins the two connections and copies bytes. Inside runs the Edge-to-Edge mutual TLS. | Neither Edge is reachable. The default: no inbound port anywhere. |

The Relay is `Apps/Axiom.Mesh.Relay`, a small LWSE application: one port, TLS with its own key, an allowlist
of Edge key fingerprints it accepts (or open, rate-limited), and a `axiom-mesh-relay/1` protocol of exactly
`Join`, `Joined` and `Refused` before it switches the connection to ProtoStream's `Raw` passthrough. It holds
nothing and sees two ciphertext streams. The Edges pin the Relay's key fingerprint in the Link configuration,
so a substituted Relay fails the handshake. `linkId` is SHA-256 of both Edge device ids sorted, so neither
side needs a shared secret and a third party cannot guess a Link without both ids.

Both transports are a dial; the Link is then an outbound peer connection in the sense of §15.1.

### 16.3 The crossing list

Each Edge holds, per Link and per direction, a list of what may cross. The list is written by the Edge's
owner through the local protocol, signed by the owner's user key and stored on the Edge; it never
replicates and the other Edge never sees it. Entries:

| Entry | Lets cross |
|---|---|
| `Package { id }` | that Package: manifest, grants, entries and objects, under the Edge's own grant |
| `User { id }` | that user's roster, so the far side can verify entries signed by that user's devices |

Nothing else exists in the first version: no wildcards, no "every Channel I own", no kinds. An empty list
is a valid Link: it comes up, authenticates, exchanges `Hello` and carries nothing, which is how a Link is
tested. The outbound list governs what the Edge *offers* over the Link (`Packages`, `Have`, `Object`,
`Roster`); the inbound list governs what it *accepts* (`HeadsChanged`, `Object`, `Roster` from the far
Edge). An item on one side only is still blocked: a Package crosses when the sending Edge lists it outbound
and the receiving Edge lists it inbound. Two owners, two decisions.

A `User` entry is needed even for Packages that user owns: a Package crossing without its author's roster
would arrive unverifiable and be dropped. The management UI adds the owner's `User` entry when a Package is
listed and says so.

### 16.4 Enforcement

The Edge module sits in the replication driver's path for Link peers only. Before any message with a
Package id, object hash or roster is written to or accepted from a Link peer, the module resolves it to a
Package or user and checks the list for that direction. A miss drops the message, counts it, and logs it.
Objects are resolved through the pins that hold them: an object pinned only by Packages that are not listed
does not cross even when requested by hash. An object shared by a listed and an unlisted Package crosses,
because the listed Package needs it; that is content-addressing, not a leak.

Every crossing is logged per Link, Package, direction and day (`Crossings`, §13), and the management UI
shows the log beside the list. The owner can see what left, when and how much.

### 16.5 Grants and the Edge

The Edge is a device of its owner. It can only hold, and therefore only carry, Packages its owner is
granted. Carrying a friend's Package across requires a grant to the Edge's owner, which is the existing
§5.3 rule and is deliberate: a device that carries bytes can read them, so it must be allowed to. Sealed
objects (§7.4) would lift that; see §15.7.

### 16.6 What does not cross

- Discovery. The far mesh's devices are not announced.
- Node-to-node connections. No node ever talks through an Edge to a node on the other side.
- Service sub-protocols (§12.5). A Link is `Open` only; `ServiceOpen` is refused on a Link. Live service
  traffic across meshes, if ever needed, is relayed by the Edge at message level with the service on the
  crossing list, so the Edge can still check every message. Open question §15.6.
- Grants, manifests and rosters of users not on the list.

### 16.7 Management implications

New in the API and UI: Links (create, transport, relay fingerprint, state, statistics), the crossing list
per Link and direction, the crossing log, and Relay servers. A node's management page says "Edge" when it
holds a Link. A Package's page lists the Links it crosses. The host configuration gains an `Edge` section
for the Link transports only; lists and logs live in LDB because they change at runtime and are audited.

Retention: an Edge keeps every listed Package by the desktop policy regardless of its device class, because
the far side may ask for any of it.

## 17. Phases

| Phase | Delivers | Done when |
|---|---|---|
| 0 | spikes for open questions 1 and 2 (`Spikes/`) | both answered in writing in this document; 1 and the Android half of 2 are, iOS waits for a Mac |
| 0b | LWSE: adopt-only ports, `TryReserve`, `AdoptAsync` (done on branch `claude/adopt-connections-f8d0e2`), merged and released | Mesh builds against that LWSE version with one server per node |
| 1 | Discovery, Model, Protocol, Core, Host with the CLI; Sync service; desktop only | two PCs on one LAN pair, share an Archive, sync a folder both ways, survive one being offline, and the honest layers are tested without sockets or disk |
| 2 | Chat service | a Channel between two users with attachments, pushed, ordered the same on both ends, shown by the CLI |
| 3 | Avalonia desktop app: management, the Sync and Chat service views, mesh analytics | the app does everything the CLI does, and shows the mesh's state live and over the last week |
| 4 | Android, then iOS, with the phone retention policy | a phone pairs by QR code, chats and opens the newest version of an Archive |
| 5 | Edges (§16): the Edge module with the direct transport and crossing lists, then the Relay and the relayed transport | two meshes exchange one listed Package in one direction and nothing else, with the log proving it |
| later | encryption at rest, FastCDC, more services | |
