using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>
/// What each <see cref="SyncStatus"/> means for a person, in one line (SYNC-SERVICE.md §7), for an app that explains a
/// failed call. The statuses of LOCAL-SPEC.md §11.1 are the app's own to explain.
/// </summary>
public static class SyncStatusText
{
    /// <summary>Whether a call's status is one the sync service defines.</summary>
    /// <param name="status">The status the call failed with.</param>
    /// <param name="sync">The sync status, or <see cref="SyncStatus.None"/>.</param>
    /// <returns><see langword="true"/> for a code of <see cref="SyncStatus"/>.</returns>
    public static bool IsSyncStatus(Status status, out SyncStatus sync)
    {
        sync = (SyncStatus)(ushort)status;
        bool known = sync is SyncStatus.AlreadyBound or SyncStatus.NotADirectory or SyncStatus.NestedBinding or SyncStatus.NotFollowed
            or SyncStatus.NotAnArchive or SyncStatus.NotBound or SyncStatus.UnknownMethod;
        if (!known)
            sync = SyncStatus.None;
        return known;
    }

    /// <summary>The line for a sync status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>One line, never empty.</returns>
    public static string Explain(SyncStatus status) => status switch
    {
        SyncStatus.None => "done",
        SyncStatus.AlreadyBound => "that directory is kept in sync already",
        SyncStatus.NotADirectory => "that is not an existing directory",
        SyncStatus.NestedBinding => "that directory lies inside a directory kept in sync, or holds one; a directory is kept in sync once, with all below it",
        SyncStatus.NotFollowed => "the Archive is not followed on this Host; follow it first",
        SyncStatus.NotAnArchive => "that Package is not an Archive; only an Archive holds a folder",
        SyncStatus.NotBound => "no directory kept in sync holds that path",
        SyncStatus.UnknownMethod => "the sync service does not know that method; update the service or the app",
        _ => $"the sync service refused with status {(ushort)status}",
    };
}
