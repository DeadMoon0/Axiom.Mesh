using System;
using System.IO;

namespace Axiom.Mesh.Sync;

/// <summary>
/// A watcher over <see cref="FileSystemWatcher"/> (SYNC-SERVICE.md §3): every name, size and write change below the directory,
/// with a 64 KiB buffer; an overflow reports lost changes (a full rescan follows). Where the system refuses a watch (the inotify
/// limit on Linux) it does not watch, and only the periodic rescan finds changes.
/// </summary>
internal sealed class FileSystemFolderWatcher : IFolderWatcher, IFolderWatcherFactory
{
    private const int BufferBytes = 64 * 1024;

    private readonly FileSystemWatcher? watcher;

    /// <summary>The factory: <see cref="Watch"/> makes the watchers.</summary>
    public FileSystemFolderWatcher()
    {
    }

    private FileSystemFolderWatcher(string directory, Action<bool> changed)
    {
        try
        {
            watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = BufferBytes,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.Attributes,
            };
            watcher.Changed += (_, _) => changed(false);
            watcher.Created += (_, _) => changed(false);
            watcher.Deleted += (_, _) => changed(false);
            watcher.Renamed += (_, _) => changed(false);
            watcher.Error += (_, _) => changed(true);
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            watcher?.Dispose();
            watcher = null;
        }
    }

    /// <inheritdoc />
    public bool Watching => watcher is not null;

    /// <inheritdoc />
    public IFolderWatcher Watch(string directory, Action<bool> changed) => new FileSystemFolderWatcher(directory, changed);

    /// <inheritdoc />
    public void Dispose() => watcher?.Dispose();
}
