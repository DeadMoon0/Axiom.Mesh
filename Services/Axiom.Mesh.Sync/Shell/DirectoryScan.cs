using System.Collections.Generic;

namespace Axiom.Mesh.Sync;

/// <summary>What one scan of a bound directory found (<see cref="IDirectoryScanner"/>).</summary>
/// <param name="Exists">Whether the directory exists.</param>
/// <param name="Files">Every regular file outside the ignored and reserved names; symlinks are never read.</param>
/// <param name="Directories">Every directory, relative with <c>/</c>.</param>
/// <param name="Problems">The paths it could not take in.</param>
internal sealed record DirectoryScan(bool Exists, IReadOnlyList<ScannedFile> Files, IReadOnlyList<string> Directories, IReadOnlyList<ScanProblem> Problems)
{
    /// <summary>The scan of a directory that does not exist.</summary>
    public static DirectoryScan Missing { get; } = new(false, [], [], []);
}
