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
