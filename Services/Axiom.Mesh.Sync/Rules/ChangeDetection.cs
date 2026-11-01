using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Sync;

/// <summary>
/// Which files of a scan must be read (SYNC-SERVICE.md §3 step 3): a file whose size and write time match its fingerprint, and
/// whose mode is the one it publishes with, is reused without a read; a new or changed file is read once it was quiet for the
/// stability time, and is busy before that; a fingerprint the scan did not find is a deletion. A fingerprint taken less than
/// <see cref="RacyWindow"/> after its file was last written is racy (the file may have changed again within the same tick of the
/// file system's clock, with the same size): such a file is read again until a fingerprint is taken later, as Git does with its
/// index. Pure, and allocation-free per unchanged file.
/// </summary>
internal static class ChangeDetection
{
    /// <summary>How long after its last write a file must be read for its fingerprint to be trusted: longer than any file system's tick.</summary>
    public static readonly TimeSpan RacyWindow = TimeSpan.FromSeconds(2);

    /// <summary>Compares a scan with the fingerprints.</summary>
    /// <param name="scan">The files the scan found.</param>
    /// <param name="known">The fingerprints, by path.</param>
    /// <param name="now">The time of the scan.</param>
    /// <param name="stability">How long a file must be quiet before it is read.</param>
    /// <param name="executableBitKnown">Whether the file system states an executable bit.</param>
    /// <returns>What changed.</returns>
    public static ChangeSet Of(IReadOnlyList<ScannedFile> scan, IReadOnlyDictionary<string, Fingerprint> known, DateTimeOffset now, TimeSpan stability, bool executableBitKnown)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(known);
        var reused = new List<Fingerprint>(scan.Count);
        var toHash = new List<ScannedFile>();
        var busy = new List<ScannedFile>();
        var seen = new HashSet<string>(scan.Count, StringComparer.Ordinal);
        long quietSince = (now - stability).UtcTicks;
        foreach (ScannedFile file in scan)
        {
            seen.Add(file.Path);
            bool hasPrint = known.TryGetValue(file.Path, out Fingerprint print);
            TreeEntryMode mode = ModeMemory.Of(file.Mode, hasPrint ? print.Mode : null, executableBitKnown);
            if (hasPrint && print.Size == file.Size && print.WriteTicks == file.WriteTicks && (print.Mode == mode || print.Mode == TreeEntryMode.Symlink) && !Racy(print))
                reused.Add(print);
            else if (file.WriteTicks > quietSince)
                busy.Add(file);
            else
                toHash.Add(file with { Mode = mode });
        }

        var deleted = new List<string>();
        foreach (string path in known.Keys)
        {
            if (!seen.Contains(path))
                deleted.Add(path);
        }

        return new ChangeSet(reused, toHash, deleted, busy);
    }

    // Taken so soon after the write that another write within the same tick would not show.
    private static bool Racy(Fingerprint print) => print.TakenTicks != long.MaxValue && print.TakenTicks - print.WriteTicks < RacyWindow.Ticks;
}
