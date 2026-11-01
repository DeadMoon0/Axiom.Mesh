namespace Axiom.Mesh.Sync;

/// <summary>
/// The mode a scanned file publishes with (SPEC.md §8.4, §11.4): where the file system has an executable bit (Linux) the scan
/// states it; where it has none (Windows) a file the branch made executable stays executable, remembered by its fingerprint, so a
/// Windows device never strips the bit a Linux device set. Pure.
/// </summary>
internal static class ModeMemory
{
    /// <summary>The mode of a scanned file.</summary>
    /// <param name="scanned">What the scan states: File, or ExecutableFile.</param>
    /// <param name="known">The mode its fingerprint remembers, or <see langword="null"/> for a new file.</param>
    /// <param name="executableBitKnown">Whether the file system has an executable bit.</param>
    /// <returns>The mode to publish.</returns>
    public static TreeEntryMode Of(TreeEntryMode scanned, TreeEntryMode? known, bool executableBitKnown) =>
        !executableBitKnown && known == TreeEntryMode.ExecutableFile ? TreeEntryMode.ExecutableFile : scanned;
}
