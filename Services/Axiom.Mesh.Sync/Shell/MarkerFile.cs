using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The marker file in a bound directory (<see cref="SyncProtocol.MarkerName"/>, SYNC-SERVICE.md §2): the binding's id and its
/// Archive, so an app finds the binding a path lies in by walking up to it. The state lives in the service's database only;
/// the scan never publishes the marker (its name is reserved).
/// </summary>
internal static class MarkerFile
{
    /// <summary>Writes the marker.</summary>
    /// <param name="directory">The bound directory.</param>
    /// <param name="id">The binding's id.</param>
    /// <param name="package">The Archive.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when written.</returns>
    public static Task WriteAsync(string directory, byte[] id, PackageId package, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(directory, SyncProtocol.MarkerName), $"{SyncProtocol.Namespace}\nbinding {Convert.ToHexString(id).ToLowerInvariant()}\npackage {package.Value.ToHex()}\n", cancellationToken);

    /// <summary>Removes the marker, if it is there.</summary>
    /// <param name="directory">The directory.</param>
    public static void Delete(string directory)
    {
        string path = Path.Combine(directory, SyncProtocol.MarkerName);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The directory went away or is read-only now: the binding ends all the same.
        }
    }
}
