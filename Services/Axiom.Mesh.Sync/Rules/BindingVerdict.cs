namespace Axiom.Mesh.Sync;

/// <summary>Whether a directory may be bound (<see cref="BindingRule.Check"/>).</summary>
internal enum BindingVerdict
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>It may.</summary>
    Allowed = 1,

    /// <summary>It is bound already.</summary>
    AlreadyBound = 2,

    /// <summary>It lies inside a bound directory, or holds one.</summary>
    Nested = 3,
}
