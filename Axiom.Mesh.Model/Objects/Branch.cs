namespace Axiom.Mesh;

/// <summary>A branch the owner names in a Manifest, with the head the owner declares for display (SPEC.md §8.7, §9.5).</summary>
/// <param name="Name">The branch name; at most 64 UTF-8 bytes.</param>
/// <param name="Head">The declared head Entry; zero when unset.</param>
[ClosedShape("The layout of a branch is fixed by SPEC.md §8.7.")]
public readonly record struct Branch(string Name, ObjectHash Head);
