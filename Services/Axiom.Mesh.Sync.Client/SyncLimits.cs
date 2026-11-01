using System;

namespace Axiom.Mesh.Sync;

/// <summary>The bounds of <c>axiom-mesh-sync/1</c> and of the service behind it (SYNC-SERVICE.md §9, LIMITS.md "Sync namespace").</summary>
public static class SyncLimits
{
    /// <summary>The most bindings one service keeps; one more is <c>Busy</c>.</summary>
    public const int Bindings = 64;

    /// <summary>The most ignore patterns of one binding.</summary>
    public const int IgnorePatterns = 64;

    /// <summary>The most bytes of one ignore pattern (a file name, SPEC.md §8.4).</summary>
    public const int PatternLength = Limits.MaxTreeNameLength;

    /// <summary>The most bytes of a path (LOCAL-SPEC.md §3.2).</summary>
    public const int PathLength = 4_096;

    /// <summary>The most entries of one directory a binding publishes (a Tree, SPEC.md §8.4); a larger one is a conflict.</summary>
    public const int EntriesPerDirectory = Limits.MaxTreeEntries;

    /// <summary>The most conflicts one <c>conflicts</c> reply carries; a page is also cut to fit <see cref="PayloadBytes"/>.</summary>
    public const int ConflictsPage = 1_024;

    /// <summary>The most bytes of a branch name (PAYLOADS.md §2).</summary>
    public const int LabelLength = Limits.MaxVersionLabelLength;

    /// <summary>The most bytes of a binding's error, as of an <c>Error.message</c> (LOCAL-SPEC.md §5.14).</summary>
    public const int ErrorLength = 256;

    /// <summary>The most bytes of a reply or an event's payload (LOCAL-SPEC.md §3.2 <c>payload</c>).</summary>
    public const int PayloadBytes = Limits.MaxPayloadLength;

    /// <summary>How long a directory must be quiet after a change before a run starts.</summary>
    public static TimeSpan DebounceQuiet { get; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a run waits at most after the first change of a burst, however busy the directory stays.</summary>
    public static TimeSpan DebounceCap { get; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a file must be unchanged before it is read; a younger one is busy and read at the next run.</summary>
    public static TimeSpan Stability { get; } = TimeSpan.FromSeconds(2);

    /// <summary>How often every binding runs although its watcher reported nothing.</summary>
    public static TimeSpan Rescan { get; } = TimeSpan.FromMinutes(10);
}
