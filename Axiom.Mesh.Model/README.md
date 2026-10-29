# Axiom.Mesh.Model

The honest core of [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh): ids, objects, Entries and their signatures,
the history's total order and the rules that decide what is valid (SPEC.md §2, §5, §8, §9). Pure functions over bytes
and keys: no socket, no disk, no clock, no randomness.

## Status

Phase 1a, in progress. This version holds the exception family (`MeshException`, `MeshFault`,
`MeshConfigurationException`) and the first bounds of `Limits`; ids, objects and signatures follow.

Namespace: `Axiom.Mesh`. Depends on `Axiom.ProtoStream` (`WireReader`/`WireWriter`) only.
