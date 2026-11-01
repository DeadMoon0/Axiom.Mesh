using System;
using System.Collections.Concurrent;

namespace Axiom.Mesh.Sync.Tests.Support;

/// <summary>Watchers a test drives itself: <see cref="Change"/> tells the binding of a directory that something in it changed.</summary>
internal sealed class FakeWatchers : IFolderWatcherFactory
{
    private readonly ConcurrentDictionary<string, Action<bool>> watched = new(StringComparer.OrdinalIgnoreCase);

    public IFolderWatcher Watch(string directory, Action<bool> changed)
    {
        watched[directory] = changed;
        return new Watcher(() => watched.TryRemove(directory, out _));
    }

    /// <summary>Reports a change in a watched directory.</summary>
    public void Change(string directory)
    {
        Assert.True(watched.TryGetValue(directory, out Action<bool>? changed), "No binding watches " + directory);
        changed!(false);
    }

    private sealed class Watcher(Action stop) : IFolderWatcher
    {
        public bool Watching => true;

        public void Dispose() => stop();
    }
}
