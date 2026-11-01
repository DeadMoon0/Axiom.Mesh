using System;

namespace Axiom.Mesh.Sync;

/// <summary>Starts a watcher per bound directory: the file system's seam, a fake in a test that drives changes itself.</summary>
internal interface IFolderWatcherFactory
{
    /// <summary>Starts watching a directory.</summary>
    /// <param name="directory">The bound directory, absolute.</param>
    /// <param name="changed">Called on every change below it, from any thread; with <see langword="true"/> when changes were lost and a full rescan is due.</param>
    /// <returns>The watcher.</returns>
    IFolderWatcher Watch(string directory, Action<bool> changed);
}
