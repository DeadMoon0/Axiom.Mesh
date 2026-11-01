namespace Axiom.Mesh.Sync;

/// <summary>
/// The statuses the sync service defines (SYNC-SERVICE.md §7, LOCAL-SPEC.md §11.2): a <c>ServiceCall</c> to <c>sync</c>
/// answers one of these as its status, with the <c>Error</c> of the service. The codes of LOCAL-SPEC.md §11.1 keep their
/// meaning there.
/// </summary>
public enum SyncStatus : ushort
{
    /// <summary>No sync status: the call succeeded, or failed with a status of LOCAL-SPEC.md §11.1.</summary>
    None = 0,

    /// <summary>The directory is bound already.</summary>
    AlreadyBound = 32_768,

    /// <summary>The path is not an existing directory given absolute.</summary>
    NotADirectory = 32_769,

    /// <summary>The directory lies inside a bound directory, or a bound directory lies inside it.</summary>
    NestedBinding = 32_770,

    /// <summary>The Package is not followed on this Host; follow it first.</summary>
    NotFollowed = 32_771,

    /// <summary>The Package is not an Archive.</summary>
    NotAnArchive = 32_772,

    /// <summary>No binding holds the directory.</summary>
    NotBound = 32_773,

    /// <summary>The service knows no such method.</summary>
    UnknownMethod = 32_774,
}
