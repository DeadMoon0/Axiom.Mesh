using System;

namespace Axiom.Mesh.Sync;

/// <summary>Watches a bound directory and says when something in it changed (SYNC-SERVICE.md §3). Disposing stops it.</summary>
internal interface IFolderWatcher : IDisposable
{
    /// <summary>Whether it watches; <see langword="false"/> when the system refused a watch (only the periodic rescan runs then).</summary>
    bool Watching { get; }
}
