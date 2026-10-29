namespace Axiom.Mesh.Local;

/// <summary>The OK body of a request whose section says "empty" (LOCAL-SPEC.md §1.3): zero bytes.</summary>
public readonly struct NoContent
{
    /// <inheritdoc />
    public override string ToString() => "(empty)";
}
