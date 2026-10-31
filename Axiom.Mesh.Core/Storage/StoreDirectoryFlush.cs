namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// Whether a rename in a store directory is made durable by flushing the directory (SPEC.md §11.1: "flushing the file
/// and, on platforms that need it, the directory"). <see cref="DirectoryObjectFiles"/> flushes after every rename;
/// <see cref="DirectoryObjectFiles.ProbeDirectoryFlush"/> tells a host which case applies, for its start log.
/// </summary>
public enum StoreDirectoryFlush
{
    /// <summary>Not probed.</summary>
    Unknown = 0,

    /// <summary>The platform makes a rename durable with the file (Windows, NTFS); no directory flush is needed.</summary>
    NotNeeded = 1,

    /// <summary>The directory was opened and <c>fsync</c>ed: a rename is durable once the write returns (Linux, macOS).</summary>
    Flushed = 2,

    /// <summary>The directory could not be flushed: a just-renamed object may be lost on power failure, never torn.</summary>
    Unsupported = 3,
}
