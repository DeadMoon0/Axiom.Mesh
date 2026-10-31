# Changelog

Every release of Axiom.Mesh, newest first. One version covers every package, the tool and the binaries; the tag is
`v<version>`. The release workflow (`.github/workflows/release.yml`) publishes the section of the tagged version as
the GitHub Release's notes, and every package's release notes point here. Protocol versions (AMP/1, local/1, pair/1)
are independent of package versions.

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
