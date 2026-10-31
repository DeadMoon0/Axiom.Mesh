![Icon](https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/Assets/Icon.svg)

# Axiom.Mesh

A base for software that runs between the devices on a local network without a server: discovery, pairing,
mutually authenticated encrypted transport, and replication of signed, content-addressed **Packages** between
nodes. Services run on top: file synchronisation and chat first, anything local later.

Part of the [Axiom](https://github.com/DeadMoon0/Axiom) family; packages and namespaces are `Axiom.Mesh*`.
Built on [ProtoStream](https://github.com/DeadMoon0/Axiom.ProtoStream) (the wire),
[LWSE](https://github.com/DeadMoon0/LWSE) (the host) and [LDB](https://github.com/DeadMoon0/LDB) (state).

## Status

**Phase 1a Fundamentals in progress.** The repository skeleton, the build and its quality gates (convention tests,
document checks, CI) are in place; every package exists with its anchor type, and the honest layers are built next.
Phase 1a is done when two PCs on one LAN pair, one creates a Package and writes an Entry through the CLI, the other
follows it and holds the same Entry hash, and this survives one PC being offline (DESIGN.md §17).

Start with [Documentation/DESIGN.md](Documentation/DESIGN.md) for the reasoning,
[Documentation/SPEC.md](Documentation/SPEC.md) for the normative mesh protocol (AMP/1) and
[Documentation/LOCAL-SPEC.md](Documentation/LOCAL-SPEC.md) for the normative local protocol between the Host and
programs on the same device (`axiom-mesh-local/1`). The spikes that answered the design's open questions are in
[Spikes](Spikes/README.md).

## Packages

| Package | What it is |
|---|---|
| `Axiom.Mesh.Model` | Ids, objects, Entries and signatures, the history's order and the rules that decide what is valid; honest. Namespace `Axiom.Mesh`. |
| `Axiom.Mesh.Protocol` | AMP/1, the mesh wire protocol, and the pairing protocol, described with ProtoStream; honest. |
| `Axiom.Mesh.Local` | `axiom-mesh-local/1`, the pipe protocol, both roles, with the client library; the one package an app or a service references. |
| `Axiom.Mesh.Discovery` | Finding nodes on the LAN: DNS-SD over mDNS, UDP broadcast, static peers. Usable alone. |
| `Axiom.Mesh.Core` | The engine: store, index, trust, mutual TLS pinned to rosters, replication, pins, the local port. |
| `Axiom.Mesh.Host` | `axiom-meshd`, the daemon: configuration, keys, the pipe, process identity. |
| `Axiom.Mesh.Testing` | An in-memory mesh of any number of nodes, with a fake clock, fake discovery and scripted peers. |
| `Axiom.Mesh.Cli` | `axiom-mesh`, the command line, a .NET tool and a user client of `Axiom.Mesh.Local`. |

Later: `Axiom.Mesh.Sync` and `Axiom.Mesh.Chat` (services), `Axiom.Mesh.App` (Avalonia), `Axiom.Mesh.Edge` and
`Axiom.Mesh.Relay` (linking meshes). Targets .NET 8 and .NET 10.

## Repository layout

| Path | Contents |
|---|---|
| `Axiom.Mesh.Model/`, `.Protocol/`, `.Local/`, `.Discovery/` | the honest layers |
| `Axiom.Mesh.Core/`, `Axiom.Mesh.Host/` | the engine and the daemon |
| `Axiom.Mesh.Testing/` | the in-memory mesh for tests |
| `Apps/Axiom.Mesh.Cli/` | the command line |
| `UnitTests/<Project>.Tests` | one test project per project (the CLI's under `UnitTests/Apps/`), each with `Rules/`, `Regression/` and `Conventions/` |
| `UnitTests/Shared/` | the convention tests every suite derives |
| `UnitTests/Axiom.Mesh.ReadmeSamples.Tests` | README blocks compile, the tests the documents name exist, repository-wide conventions |
| `Shared/` | sources compiled into every package |
| `Documentation/` | design, specifications, architecture rules and map, guarantees, conformance, limits, security |
| `AI/` | one skill file per package for coding agents |
| `Spikes/` | throwaway code that answered the design's open questions; not part of the build |

Allowed references, enforced by the convention tests: Model → ProtoStream; Protocol and Local → Model, ProtoStream;
Discovery → Model; Core → Model, Protocol, Local, Discovery, ProtoStream, LWSE, LDB; Host → Core; Testing → Host;
Cli → Local. No service and no app references Core or Host.

## Documentation

- [Architecture rules](Documentation/ARCHITECTURE.md) and the [architecture map](Documentation/ARCHITECTURE-MAP.md) of what carries each rule today.
- [Structural debt](STRUCTURAL-DEBT.md): where the code does not follow the rules yet, and when that is paid.
- [Guarantees](Documentation/GUARANTEES.md): every promise, with the test that proves it.
- [Conformance](Documentation/CONFORMANCE.md): every requirement of the specifications, with its test.
- [Limits](Documentation/LIMITS.md): every bound, its value and what happens when it is reached.
- [Security](Documentation/SECURITY.md): the threat model, every check, where it lives and its test, the TLS scan.
- [Benchmarks](Documentation/BENCHMARKS.md): every performance target, the number measured, its command and machine.

## Building and testing

The .NET 10 SDK (see `global.json`) builds both target frameworks; the .NET 8 runtime runs the net8.0 tests.
Packages restore from nuget.org only (`NuGet.config`).

```bash
dotnet build Axiom.Mesh.slnx -c Release
dotnet test Axiom.Mesh.slnx -c Release
dotnet pack Axiom.Mesh.slnx -c Release -o out
```

A package is released by pushing a tag `<Prefix>-v<version>` equal to its project's `<Version>` (for example
`Model-v0.1.0`); see `.github/workflows/release.yml`.
