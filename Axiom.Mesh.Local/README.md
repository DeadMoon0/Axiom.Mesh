# Axiom.Mesh.Local

`axiom-mesh-local/1` (LOCAL-SPEC.md), the protocol every program on a device speaks to the [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) Host over the pipe, in both roles (user and service), with the client library. The one package an app, a script or a service references.

## Status

Phase 1a, in progress: a skeleton holding the protocol's name; the messages and the client follow.

Namespace: `Axiom.Mesh.Local`. Depends on `Axiom.Mesh.Model` and `Axiom.ProtoStream`, never on the Host or its engine.
