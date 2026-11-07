# Changelog

Every release of Axiom.Mesh, newest first. One version covers every package, the tool and the binaries; the tag is
`v<version>`. The release workflow (`.github/workflows/release.yml`) publishes the section of the tagged version as
the GitHub Release's notes, and every package's release notes point here. Protocol versions (AMP/1, local/1, pair/1)
are independent of package versions.

## 0.2.0

Unreleased: the services of the plan's phases 1b and 2, tagged once Sync, Chat and the acceptance are done (S-D12). Every
project's `<Version>` is 0.2.0 already. What is on `main` so far (S0, service plumbing):

- **LOCAL-SPEC.md draft 5.** Services are programs of their own that the Host spawns and supervises (decisions 26 to
  28): each start gets a fresh token, the pipe, the Host's home and a state directory in its environment (§4.2), and
  the token lasts for that child's connections until it exits. `ServiceCall` runs beside the caller's other requests
  (§3.4, §6.7); a reply the Host cannot pass on is a violation of the service's connection (§9.8); the key that releases
  a service's pins on disconnect is named (§9.6); `Dropped` (§14.12) and `vectors/local.txt` (§15) are part of it.
- **The Host serves `ServiceCall`, `Emit`, `UpdatePackage` and `AddTelemetry`** (the last `NotFound`, like the other
  telemetry requests). Every request of LOCAL-SPEC.md §14.1 now has a handler.
- **The Host spawns services.** `services.<name>.spawn` (an absolute path, or a bare name found beside `axiom-meshd`),
  `args` and `releasePinsOnDisconnect` in `config.json`; restarts with backoff, gives up loudly after five crashes in
  five minutes or a configuration error, drains then kills on stop. The default `config.json` lists `chat` and `sync`
  (`axiom-mesh-chat`, `axiom-mesh-sync`); an entry whose executable is not there is noted once and left alone.
- **New package `Axiom.Mesh.Service`**: the runtime every service is built on (`IMeshService`, `ServiceProgram`,
  `ServiceSession`, `ServiceState` with its cursor, catch-up, reconnects, calls).
- `Axiom.Mesh.Local`: `CallbackHandler` and `LocalClientOptions.Callbacks` (a service serves `ForwardedCall`s off its
  read loop), `CallReply`, `ReplyStatusRule`, `CallbackReplyRule`.
- `Axiom.Mesh.Core`: `LocalPort.SetSpawnedToken`/`ClearSpawnedToken`, `LocalService.ReleasePinsOnDisconnect`. Fixed: an
  Entry stored while a `Subscribe` to topic entries planned its replay could reach that subscription neither replayed
  nor live (LOCAL-SPEC.md §7.2).
- `Axiom.Mesh.Testing`: `TestNode.RunServiceAsync`, `IMeshFixture.RunServiceAsync`, `TestServices`, `TestEchoService`,
  `RunningService`; scenario 15 (a service beside a node).
- `axiom-mesh services [--json]`: the services the Host lists, how each proves its name, whether it is connected.

S1, the chat service:

- **The chat service `axiom-mesh-chat`** ([CHAT-SERVICE.md](Documentation/CHAT-SERVICE.md) draft 1): Channels, messages
  with attachments and replies, withdrawals, indexed in its own database (`<home>/services/chat/index`) and offered as the
  namespace `axiom-mesh-chat/1` (ten methods, four events, six statuses). Ships beside `axiom-meshd` in the zips (Native
  AOT); the Host's default configuration starts it.
- **New package `Axiom.Mesh.Chat.Client`**: the namespace's records, `ChatCall`, `ChatEventReader`, `ChatStatus` and
  `ChatStatusText`, `ChatLimits`, `ChatCodec`; test vectors in `Documentation/vectors/chat/records.txt`.
- **Command line:** `chat list`, `chat show`, `chat thread`, `chat send` (with `--file` once per attachment and
  `--reply`), `chat withdraw`, `chat get`, `chat watch`. `write` goes through the chat service when it runs and writes
  directly when it does not; `package publish` explains `KindTaken` (the sync service holds Archives).
- `Axiom.Mesh.Local`: `StoreFiles.MaterialiseFileAsync` writes one file from its ChunkList (never partial under its name),
  `MaterialisedFile`.
- `Axiom.Mesh.Service`: `IMeshService.OnEntriesAsync` hands a service each page of the catch-up at once (by default one
  `OnEntryAsync` per Entry); the cursor is committed lazily durable after a page or a lull instead of after every Entry.
- `Axiom.Mesh.Testing`: `IMeshFixture.RunServiceAsync(name, service, stateOf, ct)` and
  `TestNode.RunServiceAsync(service, stateOf, ct)` run a service with another node's kept state (another store epoch).

S2, the sync service:

- **The sync service `axiom-mesh-sync`** ([SYNC-SERVICE.md](Documentation/SYNC-SERVICE.md) draft 1): a folder bound to an
  Archive and one of its branches, kept in step both ways. Local changes are published as Versions naming their parents
  explicitly; concurrent leaves are merged by SPEC.md §9.5 (pays STRUCTURAL-DEBT.md 5); a path changed on two devices
  keeps the later leaf's file and the other side's file or subtree beside it as `<name>.conflict-<8 hex>`; every run is
  crash-safe (the target recorded before the disk changes, the base after). Its own database
  (`<home>/services/sync/index`), the namespace `axiom-mesh-sync/1` (six methods, two events, seven statuses). Ships beside
  `axiom-meshd` in the zips (Native AOT); the Host's default configuration starts it.
- **New package `Axiom.Mesh.Sync.Client`**: the namespace's records, `SyncCall`, `SyncEventReader`, `SyncStatus` and
  `SyncStatusText`, `SyncLimits`, `SyncCodec`, `SyncProtocol`; test vectors in `Documentation/vectors/sync/records.txt`.
- **Command line:** `sync add <dir> (--package <id|name> | --create <name>) [--label] [--symlinks asfile|ignore]
  [--ignore …]`, `sync remove`, `sync list`, `sync status [<dir>]`, `sync scan [<dir>]`, `sync conflicts [<dir>]`,
  `sync watch`. `host install` adds `services.sync` to a `config.json` an older Host wrote (`--no-sync` leaves it out);
  `package publish` points at `sync add` when the sync service holds Archives.
- `Axiom.Mesh.Local`: the file helpers' pure parts are public (STRUCTURAL-DEBT.md 55 narrowed): `FileNameRule` with
  `ReservedPrefix` (`.axiom-mesh-` names are never published nor written), `FolderTrees.TryBuild` over `FolderFile`s,
  `MaterialisePlan`, `PlannedFile`, `PlannedFileKind`, `ObjectReader`; `StoreFiles.WriteFileAsync` writes one planned file.
- `Axiom.Mesh.Service`: a call that reaches a service while it connects waits until `OnConnectedAsync` returned (it was
  answered by each service's own gate).
- `Axiom.Mesh.Testing`: `TestMesh.ConnectAsync` steps the fake clock past a backoff both peers hold, so a pair severed
  and healed always reconnects.
- `Axiom.Mesh.Core`, fixed: a `Fetch` whose change subscription lapsed (thousands of objects stored while it ran) no longer
  waits forever once every missing object has a source; it subscribes again and looks once more.

S3, pairing without a code (after the first two-PC run, 2026-11-02):

- **PAIR-SPEC.md draft 3.** The entering side may name the showing device by the id discovery announced instead of a
  code (§2.4): the pin is the device id, `H` of the key the handshake must present (§4.2), and the SAS stays the check
  on both screens (§10). A code lists the addresses of the interfaces with a default route when any has one (§2.2), so
  a virtual switch or a VPN adapter no longer lengthens it.
- **LOCAL-SPEC.md draft 6.** `StartPairingByDevice` (278, §6.2, decision 29): the mode, the device id and the addresses
  `ListDiscovered` shows; `StartPairing` is unchanged (§1.5). `Documentation/vectors/local.txt` was regenerated: the new
  sample shifts the correlation of the samples after it, no published layout changed.
- **Command line:** `pair join` without a target lists the discovered devices not paired yet (`DEVICE`, `USER`,
  `ADDRESSES`, `LAST SEEN`) and refuses in one line when there are none; `pair join <device id or prefix>` pairs
  without a code; `pair join <code>` as before. `pair show` says first `on the other PC run: axiom-mesh pair join
  <this device's id>`.
- `Axiom.Mesh.Protocol`: `PairingTarget` (`OfCode`, `OfDevice`, `Device`, `Addresses`, `Code`, `IsByCode`, `Admits`).
- `Axiom.Mesh.Core`: `PeerCheck` admits the server key of the entering dial by its hash against the pinned id, which
  covers a code's key too.
- `Axiom.Mesh.Testing`: `IMeshFixture.PairByDiscoveryAsync`, `TestMesh.PairByDiscoveryAsync`, `MeshPairing.RunAsync`
  with a `PairingEntry` (`ByCode`, `ByDiscovery`), `MeshPairing.ByDiscoveryAsync`; scenario 1b (pair by discovery), in
  memory and on sockets.
- `Axiom.Mesh.Service`, fixed (STRUCTURAL-DEBT.md 81, acceptance run 2026-11-02 item 1): a service the Host spawned
  before `identity create` kept the empty identity of its `HelloAck` until it reconnected, so the Channel owner's own
  device was refused `chat withdraw` and the sync service read a zero device. `ServiceSession.Identity` is the Host's
  identity as it stands (`null` while there is none), kept current by the runtime over topic `users` and `GetIdentity`
  (LOCAL-SPEC.md §9.1 says so); `ServiceSession.Host` stays the `HelloAck`, for the store epoch and sequence. Chat's
  `withdraw` and Sync's Versions decide by it; a sync run on a Host without an identity ends `Error` and writes nothing.
- Not done: an announcement says neither that a pairing is open nor the device's name, so `pair join` lists every
  unknown device by id (STRUCTURAL-DEBT.md 80).
- `Axiom.Mesh.Core`, fixed (STRUCTURAL-DEBT.md 86, the first `tls-scan` run): on Linux the mesh port issued a TLS 1.3
  session ticket SPEC.md §4.2 forbids, because .NET's `AllowTlsResume = false` sets only `SSL_OP_NO_TICKET`, which in
  TLS 1.3 means a stateful ticket, not none. The server handshake now sets its ticket count to zero
  (`SSL_set_num_tickets`) on OpenSSL; Windows is unchanged. Proved on Linux by
  `TlsConformanceTests.OnLinuxTheMeshPortIssuesNoSessionTicket`.

## 0.1.0

The first release: the Fundamentals of [the plan's phase 1a](Documentation/DESIGN.md) (DESIGN.md §17). Two desktops on
one LAN pair, write Entries (a text message, a published folder) and replicate them, offline and back.

**Read this first if you ran a build from `main` before 0.1.0.**

- **The Entry layout changed in SPEC.md draft 5.** An Entry now carries `references` and `redacts` in its signed
  envelope, and the Host parses no payload ([SPEC.md](Documentation/SPEC.md) §8.6,
  [PAYLOADS.md](Documentation/PAYLOADS.md)). Every signed test vector in `Documentation/vectors/` was regenerated;
  an Entry signed before draft 5 does not verify any more.
- **The store's schema version was bumped to 2.** A Host refuses a store written by a pre-release build at start
  (`MeshStoreException`, exit 4) and says what to do: there is no migration before 0.1.0, so stop the Host, delete the
  pre-release data and keys directories, start it again and run `axiom-mesh identity create`; pair again. The
  schema bump also covers the `Parents` table of the late-parent check.
- **A dropped Package is announced.** LOCAL-SPEC.md §14.12 gains change kind 8, `Dropped`: topic `packages` reports it
  when a Package no peer lists any more is dropped (after an `Unfollowed`, or when the last lister omits it).

What is in it:

- Packages on nuget.org: `Axiom.Mesh.Local` (the client library for programs that talk to the Host),
  `Axiom.Mesh.Core` (the engine, to embed a node), `Axiom.Mesh.Testing` (an in-memory mesh for tests), their building
  blocks `Axiom.Mesh.Model`, `Axiom.Mesh.Protocol` and `Axiom.Mesh.Discovery`, and the dotnet tool `axiom-mesh`.
- Binaries on the GitHub Release: one zip per platform (win-x64, linux-x64) with `axiom-meshd` and `axiom-mesh`,
  self-contained Native AOT, and `SHA256SUMS`.
- One-line install (`install.ps1`, `install.sh`) that verifies the checksum, puts both on the PATH and registers the
  Host to start at logon; `axiom-mesh host install|uninstall` does the same registration by hand.
- The command line reads `pipe.name` from `config.json`, so it finds a Host configured with a pipe of its own.
- On Linux and macOS a stored object's directory is flushed after the rename (`fsync`), so a just-stored object
  survives a power failure; the Host logs at start how renames are made durable.

Known limits are listed in [STRUCTURAL-DEBT.md](STRUCTURAL-DEBT.md) (not hidden, rule 3 of its preamble), among them:
signatures are not deterministic (1), mDNS is checked by hand only (2), no key backup (4), no encryption at rest (9),
replication over loopback at about half its throughput target (60), signing at about 6 300 Entries per second (61).
Security reports: [SECURITY.md](Documentation/SECURITY.md).
