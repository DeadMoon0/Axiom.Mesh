![Icon](https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/Assets/Icon.svg)

# Axiom.Mesh

A base for software that runs between the devices on a local network without a server: discovery, pairing,
mutually authenticated encrypted transport, and replication of signed, content-addressed **Packages** between
nodes. Each device runs one **Host** (`axiom-meshd`) that holds the keys, the store and the connections; programs on
the device talk to it over a local pipe. Services run on top: file synchronisation and chat first, anything local later.

Part of the [Axiom](https://github.com/DeadMoon0/Axiom) family; packages and namespaces are `Axiom.Mesh*`.
Built on [ProtoStream](https://github.com/DeadMoon0/Axiom.ProtoStream) (the wire),
[LWSE](https://github.com/DeadMoon0/LWSE) (the host) and [LDB](https://github.com/DeadMoon0/LDB) (state).

## Install

The Host and the command line, self-contained, for the current user; the Host is registered to start at every logon
(`-NoAutostart` / `--no-autostart` opts out):

```text
Windows (PowerShell):  irm https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/install.ps1 | iex
Linux:                 curl -fsSL https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/install.sh | sh
```

The script downloads the latest release's zip for your platform and `SHA256SUMS`, refuses a zip that does not match,
puts `axiom-meshd` and `axiom-mesh` on your PATH, registers and starts the Host and prints `axiom-mesh status`. The
script is fetched from `main`, which can change; the checksum protects the binaries, not the script. For the strict
option fetch it from a release tag (`…/Axiom.Mesh/v0.1.0/install.ps1`). Then:

```text
axiom-mesh identity create --user <your name> --device <this device>
axiom-mesh pair show                (on one device; on the other: axiom-mesh pair join <code>)
axiom-mesh help
```

[ACCEPTANCE.md](Documentation/ACCEPTANCE.md) walks two PCs through pairing, writing, publishing a folder and catching up
after being offline. With the .NET SDK, the command line alone is also a tool: `dotnet tool install -g Axiom.Mesh.Cli`
(it then needs the Host's executable from a release; `axiom-mesh host install --daemon <path>`).

## Status

**0.1.0, phase 1a Fundamentals** (DESIGN.md §17): two PCs on one LAN pair, one creates a Package and writes an Entry
through the command line (a message, a published folder), the other follows it and holds the same Entry hashes, and
this survives one PC being offline. Windows and Linux desktops. What is not built yet, and why, is in
[STRUCTURAL-DEBT.md](STRUCTURAL-DEBT.md); what changed is in [CHANGELOG.md](CHANGELOG.md).

**On `main`, towards 0.2.0 (phases 1b and 2):** the plumbing for services is in (S0): the Host spawns and supervises
services, serves `ServiceCall`, `Emit` and `UpdatePackage`, and `Axiom.Mesh.Service` is the runtime a service is built
on; `axiom-mesh services` lists them. The chat service is in (S1): `axiom-mesh-chat` indexes Channels and messages with
attachments, replies and withdrawals and offers them as the namespace `axiom-mesh-chat/1`
([CHAT-SERVICE.md](Documentation/CHAT-SERVICE.md)); `axiom-mesh chat ...` uses it. The Sync service comes next.

## What is released

| What | Where | For |
|---|---|---|
| `Axiom.Mesh.Local` | NuGet | Programs that talk to the Host: the client library (`LocalClient`), every request and record of the local protocol, the pipe, the service role, and the file helpers (`StoreFiles`, `Trees`). |
| `Axiom.Mesh.Core` | NuGet | Embedding a node: the engine (store, trust, mutual TLS, replication, the local port) with everything dishonest injected. |
| `Axiom.Mesh.Service` | NuGet (from 0.2.0) | Building a service: the runtime that connects it to the Host, catches it up on every Entry of its kinds, keeps it connected, hands it apps' calls and keeps its LDB state. |
| `Axiom.Mesh.Chat.Client` | NuGet (from 0.2.0) | Apps over the chat service: the records of `axiom-mesh-chat/1`, `ChatCall`, `ChatEventReader`, its statuses and limits. |
| `Axiom.Mesh.Testing` | NuGet | Tests of services and apps: an in-memory mesh of any number of nodes on one fake clock, scripted peers, the command line in-process, services run beside a node. |
| `Axiom.Mesh.Model`, `.Protocol`, `.Discovery` | NuGet | Building blocks of Core and Local, published as their dependencies; not referenced directly. |
| `Axiom.Mesh.Cli` | NuGet dotnet tool `axiom-mesh` | The command line for developers with the .NET SDK. |
| `axiom-meshd` + `axiom-mesh` (+ `axiom-mesh-chat` from 0.2.0) | GitHub Release, one zip per platform (win-x64, linux-x64) with `SHA256SUMS` | Everyone: self-contained Native AOT executables; what the install scripts fetch. The Host is never a NuGet package. |

One version covers everything; a tag `v<version>` equal to every project's `<Version>` publishes it all through
`.github/workflows/release.yml`. Protocol versions (AMP/1, local/1, pair/1) are independent of package versions.
Targets .NET 8 and .NET 10. Later: `Axiom.Mesh.Sync` (a service), `Axiom.Mesh.App` (Avalonia),
`Axiom.Mesh.Edge` and `Axiom.Mesh.Relay` (linking meshes).

## Documentation

- [DESIGN.md](Documentation/DESIGN.md): the reasoning.
- [SPEC.md](Documentation/SPEC.md): AMP/1, the mesh protocol (normative). [PAYLOADS.md](Documentation/PAYLOADS.md): the
  payload formats services put into Entries. [CHAT-SERVICE.md](Documentation/CHAT-SERVICE.md): the chat service. [PAIR-SPEC.md](Documentation/PAIR-SPEC.md): pairing.
- [LOCAL-SPEC.md](Documentation/LOCAL-SPEC.md): `axiom-mesh-local/1`, between the Host and programs on the same device
  (normative). [PIPE-ACCESS.md](Documentation/PIPE-ACCESS.md): who may open the pipe, per platform.
- [ARCHITECTURE.md](Documentation/ARCHITECTURE.md): the rules; [ARCHITECTURE-MAP.md](Documentation/ARCHITECTURE-MAP.md):
  what carries each rule today.
- [GUARANTEES.md](Documentation/GUARANTEES.md): every promise, with the test that proves it.
- [CONFORMANCE.md](Documentation/CONFORMANCE.md): every requirement of the specifications, with its test.
- [LIMITS.md](Documentation/LIMITS.md): every bound, its value and what happens when it is reached.
- [SECURITY.md](Documentation/SECURITY.md): the threat model, every check and its test, the TLS scan, how to report.
- [BENCHMARKS.md](Documentation/BENCHMARKS.md): every performance target, the number measured, its command and machine.
- [ACCEPTANCE.md](Documentation/ACCEPTANCE.md): the two-PC acceptance run, with its run log.
- [STRUCTURAL-DEBT.md](STRUCTURAL-DEBT.md) and [CHANGELOG.md](CHANGELOG.md).
- Per package: its README (Quick Start blocks compile in `ReadmeSamples.Tests`), and `AI/<Package>.SKILL.md` for coding agents.

## Repository layout

| Path | Contents |
|---|---|
| `Axiom.Mesh.Model/`, `.Protocol/`, `.Local/`, `.Discovery/` | the honest layers |
| `Axiom.Mesh.Core/`, `Axiom.Mesh.Host/` | the engine and the daemon |
| `Axiom.Mesh.Service/` | the runtime every service is built on |
| `Axiom.Mesh.Testing/` | the in-memory mesh for tests |
| `Apps/Axiom.Mesh.Cli/` | the command line |
| `UnitTests/<Project>.Tests` | one test project per project (the CLI's under `UnitTests/Apps/`), each with `Rules/`, `Regression/` and `Conventions/` |
| `UnitTests/Shared/` | the convention tests every suite derives |
| `UnitTests/Axiom.Mesh.ReadmeSamples.Tests` | README blocks compile, the tests the documents name exist, repository-wide conventions |
| `IntegrationTests/` | the scenario suite on real sockets, TLS, disk and pipe; the release floors |
| `Benchmarks/`, `Conformance/` | the benchmark suite; the TLS conformance scan |
| `Scripts/` | the AOT smoke, the release packaging, the benchmark comparison |
| `install.ps1`, `install.sh` | the one-line install |
| `Shared/` | sources compiled into every package |
| `Documentation/` | design, specifications, architecture rules and map, guarantees, conformance, limits, security, acceptance |
| `AI/` | one skill file per package for coding agents |
| `Spikes/` | throwaway code that answered the design's open questions; not part of the build |

Allowed references, enforced by the convention tests: Model → ProtoStream; Protocol and Local → Model, ProtoStream;
Discovery → Model; Core → Model, Protocol, Local, Discovery, ProtoStream, LWSE, LDB; Host → Core; Service → Local,
Model, ProtoStream, LDB; Testing → Core, Service and the command line (packed inside it); Cli → Local. No service and no
app references Core or Host. `UnitTests/Support/` holds programs the suites start as child processes (the echo service).

## Building and testing

The .NET 10 SDK (see `global.json`) builds both target frameworks; the .NET 8 runtime runs the net8.0 tests.
Packages restore from nuget.org only (`NuGet.config`).

```bash
dotnet build Axiom.Mesh.slnx -c Release
dotnet test Axiom.Mesh.slnx -c Release
dotnet pack Axiom.Mesh.slnx -c Release -o out
pwsh Scripts/package.ps1 -Output dist        # or: sh Scripts/package.sh linux-x64 dist
```

The last line publishes both executables with Native AOT, runs the smoke and builds the release zip with `SHA256SUMS`;
`install.ps1 -Source dist/axiom-mesh-<version>-win-x64.zip` (or `install.sh --source …`) installs it.
