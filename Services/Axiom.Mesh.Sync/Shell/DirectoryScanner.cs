using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The scanner over the real file system (SYNC-SERVICE.md §3 step 2): one directory listing at a time, each entry's size,
/// write time and, outside Windows, executable bit from the listing; symlinks and other reparse points are never followed nor
/// read; ignored and reserved names are left out; a name a Tree cannot hold, a directory of more than 8 192 entries and a
/// directory that cannot be listed are problems, whose contents stay as they are.
/// </summary>
internal sealed class DirectoryScanner : IDirectoryScanner
{
    /// <inheritdoc />
    public DirectoryScan Scan(string directory, IgnoreRule ignore, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(ignore);
        if (!Directory.Exists(directory))
            return DirectoryScan.Missing;
        var files = new List<ScannedFile>();
        var directories = new List<string>();
        var problems = new List<ScanProblem>();
        bool executableBit = !OperatingSystem.IsWindows();
        var pending = new Stack<(DirectoryInfo Directory, string Prefix)>([(new DirectoryInfo(directory), string.Empty)]);
        while (pending.TryPop(out (DirectoryInfo Directory, string Prefix) next))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<FileSystemInfo> entries;
            try
            {
                entries = [.. next.Directory.EnumerateFileSystemInfos("*", Options)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                problems.Add(new ScanProblem(next.Prefix.TrimEnd('/'), ConflictKind.Unreadable));
                continue;
            }

            if (entries.Count > SyncLimits.EntriesPerDirectory)
            {
                problems.Add(new ScanProblem(next.Prefix.TrimEnd('/'), ConflictKind.TooManyEntries));
                continue;
            }

            foreach (FileSystemInfo entry in entries)
            {
                string path = next.Prefix + entry.Name;
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint) || ignore.Matches(path))
                    continue;
                if (!TreeName.MayHold(entry.Name))
                {
                    problems.Add(new ScanProblem(path, ConflictKind.NameRefused));
                    continue;
                }

                if (entry is DirectoryInfo sub)
                {
                    directories.Add(path);
                    pending.Push((sub, path + "/"));
                }
                else if (entry is FileInfo file)
                {
                    TreeEntryMode mode = executableBit && (file.UnixFileMode & UnixFileMode.UserExecute) != 0 ? TreeEntryMode.ExecutableFile : TreeEntryMode.File;
                    files.Add(new ScannedFile(path, mode, (ulong)file.Length, file.LastWriteTimeUtc.Ticks));
                }
            }
        }

        return new DirectoryScan(true, files, directories, problems);
    }

    private static EnumerationOptions Options { get; } = new() { RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false, ReturnSpecialDirectories = false };
}
