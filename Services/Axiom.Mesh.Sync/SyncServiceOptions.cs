using System;

namespace Axiom.Mesh.Sync;

/// <summary>How a <see cref="SyncService"/> runs: its clock and how long a file must be quiet before it is read.</summary>
public sealed record SyncServiceOptions
{
    /// <summary>The clock the debounce, the periodic rescan and the recorded times run on; a test's fake one moves only when told.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>
    /// How long a file must be unchanged, by its write time against the system's clock, before a run reads it; a younger one is
    /// read at a later run. <see cref="SyncLimits.Stability"/> by default; a test that writes files and scans at once sets zero.
    /// </summary>
    public TimeSpan Stability { get; init; } = SyncLimits.Stability;

    /// <summary>The default options: the system clock, a stability of two seconds.</summary>
    public static SyncServiceOptions Default { get; } = new();

    /// <summary>The scanner of bound directories; the real file system unless a test replaces it.</summary>
    internal IDirectoryScanner Scanner { get; init; } = new DirectoryScanner();

    /// <summary>The watchers of bound directories; the real file system unless a test replaces it.</summary>
    internal IFolderWatcherFactory Watchers { get; init; } = new FileSystemFolderWatcher();

    /// <summary>Where a test stops a run as a crash would (SYNC-SERVICE.md §3); never in a real run.</summary>
    internal Func<RunStep, bool> CrashAt { get; init; } = static _ => false;
}
