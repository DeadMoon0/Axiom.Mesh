namespace Axiom.Mesh.Sync;

/// <summary>Where a binding stands (SYNC-SERVICE.md §4).</summary>
public enum BindingState : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>The directory equals the branch head as far as the service knows; nothing to do.</summary>
    InSync = 1,

    /// <summary>A run reads the directory.</summary>
    Scanning = 2,

    /// <summary>A run publishes the changes of the directory as a Version.</summary>
    Pushing = 3,

    /// <summary>A run fetches, merges and writes the changes of the branch into the directory.</summary>
    Pulling = 4,

    /// <summary>A read-only binding whose directory holds changes the service may not publish; they stay as they are.</summary>
    LocalChangesReadOnly = 5,

    /// <summary>A Tree or file the branch needs is not stored here yet, and no peer supplied it; the next run tries again.</summary>
    WaitingForObjects = 6,

    /// <summary>The binding cannot run: its directory is gone, its Archive is no longer followed, or a run failed.</summary>
    Error = 7,
}
