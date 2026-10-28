![Icon](https://raw.githubusercontent.com/DeadMoon0/Axiom/refs/heads/main/Assets/Icon.svg)

# Axiom.Mesh

A base for software that runs between the devices on a local network without a server: discovery, pairing,
mutually authenticated encrypted transport, and replication of signed, content-addressed **Packages** between
nodes. Services run on top: file synchronisation and chat first, anything local later.

Part of the [Axiom](https://github.com/DeadMoon0/Axiom) family; packages and namespaces are `Axiom.Mesh*`.
Built on [ProtoStream](https://github.com/DeadMoon0/Axiom.ProtoStream) (the wire),
[LWSE](https://github.com/DeadMoon0/LWSE) (the host) and [LDB](https://github.com/DeadMoon0/LDB) (state).

## Status

Design. Nothing is implemented yet. Start with [Documentation/DESIGN.md](Documentation/DESIGN.md) for the
reasoning, [Documentation/SPEC.md](Documentation/SPEC.md) for the normative mesh protocol (AMP/1) and
[Documentation/LOCAL-SPEC.md](Documentation/LOCAL-SPEC.md) for the normative local protocol between the Host
and programs on the same device (`axiom-mesh-local/1`); the spikes that answer the design's open questions are
in [Spikes](Spikes/README.md).

## Planned packages

| Package | What it is |
|---|---|
| `Axiom.Mesh.Discovery` | Finding nodes on the LAN: mDNS/DNS-SD, UDP broadcast, static peers. Usable alone. |
| `Axiom.Mesh.Model` | Objects, entries, signing, the history DAG, order and merge rules. |
| `Axiom.Mesh.Protocol` | The `axiom-mesh/1` wire protocol, described with ProtoStream. |
| `Axiom.Mesh.Core` | The engine: store, Package registry, rosters, grants, mutual TLS, replication, pins. |
| `Axiom.Mesh.Host` | The node daemon with its management API and web UI. |
| `Axiom.Mesh.Sync` | File and folder synchronisation as a service. |
| `Axiom.Mesh.Chat` | Chat with attachments as a service. |
| `Axiom.Mesh.App` | The Avalonia app for desktop and mobile. |
| `Axiom.Mesh.Edge` | Later: linking two meshes through a node that carries only what its owner listed. |
| `Axiom.Mesh.Relay` | Later: a relay server for Edges that cannot reach each other directly. |
