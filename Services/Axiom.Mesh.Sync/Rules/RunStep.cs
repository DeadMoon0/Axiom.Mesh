namespace Axiom.Mesh.Sync;

/// <summary>
/// The points of a run where a crash leaves the most to recover (SYNC-SERVICE.md §3), named so a test can stop a run there and
/// prove that the next run repeats nothing and loses nothing.
/// </summary>
internal enum RunStep
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>A Version was written, its base not yet committed (step 8).</summary>
    Published = 1,

    /// <summary>The target was recorded as pending, nothing written to disk yet (step 10).</summary>
    PendingRecorded = 2,

    /// <summary>One file of the target was written, the others not (step 11).</summary>
    FileWritten = 3,

    /// <summary>The base was committed, the merge Version not yet written (step 12).</summary>
    BaseCommitted = 4,

    /// <summary>A merge Version was written, the base not yet moved to it (step 13).</summary>
    MergeWritten = 5,
}
