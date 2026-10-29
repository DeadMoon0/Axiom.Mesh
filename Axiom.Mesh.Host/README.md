# Axiom.Mesh.Host

`axiom-meshd`, the [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) daemon: the one process on a device that holds the keys, the store, the database and every connection. It reads its configuration, keeps its keys, and opens the pipe its clients connect to.

## Status

Phase 1a, in progress: `axiom-meshd version` prints the daemon's version; `run` follows with the engine.

Namespace: `Axiom.Mesh.Host`. Depends on `Axiom.Mesh.Core`.
