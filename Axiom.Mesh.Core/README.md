# Axiom.Mesh.Core

The engine of an [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) node: the content-addressed store and its index, trust in rosters and grants, mutual TLS pinned to the rosters, replication with peers, pins and eviction, and the local port every client of the pipe reaches. Everything dishonest (files, database, keys, clock, sockets, discovery) is injected.

## Status

Phase 1a, in progress: a skeleton; the store, the transport, replication and the local port follow.

Namespace: `Axiom.Mesh.Core`. Depends on Model, Protocol, Local, Discovery, ProtoStream, LDB and (from LWSE 0.3.0) LWSE Core.
