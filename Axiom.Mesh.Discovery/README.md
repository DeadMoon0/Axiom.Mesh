# Axiom.Mesh.Discovery

Finding [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) nodes on a local network (SPEC.md §3): DNS-SD over mDNS, UDP broadcast and static peers, as pure codecs with thin socket shells and no library. A discovered address is a hint, never trusted: only the TLS handshake proves who answers.

## Status

Phase 1a, in progress: a skeleton holding the DNS-SD service type; the codecs and shells follow.

Namespace: `Axiom.Mesh.Discovery`. Depends on `Axiom.Mesh.Model` for the ids; usable alone.
