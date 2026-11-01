using System.Threading;

namespace Axiom.Mesh.Sync;

/// <summary>Reads a bound directory's entries without reading a file's bytes (SYNC-SERVICE.md §3 step 2). The file system's seam.</summary>
internal interface IDirectoryScanner
{
    /// <summary>Scans a directory.</summary>
    /// <param name="directory">The bound directory, absolute.</param>
    /// <param name="ignore">The names to leave alone.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>What it holds.</returns>
    DirectoryScan Scan(string directory, IgnoreRule ignore, CancellationToken cancellationToken);
}
