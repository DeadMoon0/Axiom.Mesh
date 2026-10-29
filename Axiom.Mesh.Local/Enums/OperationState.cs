namespace Axiom.Mesh.Local;

/// <summary>The state of an operation (LOCAL-SPEC.md §8.2, §14.7).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum OperationState : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Work is in progress.</summary>
    Running = 1,

    /// <summary>Ended; <c>Result</c> holds the kind's result body.</summary>
    Done = 2,

    /// <summary>Ended; <c>Status</c> holds the reason.</summary>
    Failed = 3,

    /// <summary>Ended by <c>CancelOperation</c>, <c>Drain</c> or the connection's end.</summary>
    Cancelled = 4,
}
