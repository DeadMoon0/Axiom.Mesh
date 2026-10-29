namespace Axiom.Mesh.Core;

/// <summary>One object the planner says to want, and which step asked for it.</summary>
/// <param name="Hash">The object.</param>
/// <param name="Kind">The step of SPEC.md §10.2 or §10.3.</param>
internal readonly record struct PlannedWant(ObjectHash Hash, WantKind Kind);
