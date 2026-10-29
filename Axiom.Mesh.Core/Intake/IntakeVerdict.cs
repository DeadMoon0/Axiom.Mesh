namespace Axiom.Mesh.Core;

/// <summary>What intake made of an object a peer or a client sent (SPEC.md §10.6, §12).</summary>
internal enum IntakeVerdict
{
    /// <summary>Dropped without blame: not wanted, or an older state than the one held.</summary>
    Dropped = 0,

    /// <summary>Handed to the store writer.</summary>
    Admitted = 1,

    /// <summary>Held until what it needs arrives (SPEC.md §10.6).</summary>
    Held = 2,

    /// <summary>Faulty content: malformed, a bad signature, an invalid Entry; dropped and its sender recorded (SPEC.md §12.2).</summary>
    Faulty = 3,

    /// <summary>A protocol violation: the bytes are not the object the sender named (SPEC.md §7.6, §12.1).</summary>
    Violation = 4,
}
