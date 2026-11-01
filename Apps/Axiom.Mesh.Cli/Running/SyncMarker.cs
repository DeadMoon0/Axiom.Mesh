using System;
using System.IO;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The bound directory a path lies in, found as git finds its repository: walking up from the path to the first directory that
/// holds the sync service's marker file (<see cref="SyncProtocol.MarkerName"/>). Pure but for the existence check it is given.
/// </summary>
internal static class SyncMarker
{
    /// <summary>The first directory at or above a path that holds the marker.</summary>
    /// <param name="start">The path, absolute.</param>
    /// <param name="exists">Whether a file exists.</param>
    /// <returns>The directory, or <see langword="null"/> when none above holds a marker.</returns>
    public static string? Find(string start, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrEmpty(start);
        ArgumentNullException.ThrowIfNull(exists);
        for (string? directory = Path.TrimEndingDirectorySeparator(start); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
        {
            if (exists(Path.Combine(directory, SyncProtocol.MarkerName)))
                return directory;
        }

        return null;
    }
}
