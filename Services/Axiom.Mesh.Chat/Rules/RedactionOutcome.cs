namespace Axiom.Mesh.Chat;

/// <summary>What <see cref="RedactionApplication"/> decided for one redaction and one target.</summary>
internal enum RedactionOutcome
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>The redaction is honoured: the target is withdrawn.</summary>
    Applied = 1,

    /// <summary>The target is not indexed yet; the redaction waits for it.</summary>
    Pending = 2,

    /// <summary>SPEC.md §9.4 does not honour it: the target stays.</summary>
    Ignored = 3,
}
