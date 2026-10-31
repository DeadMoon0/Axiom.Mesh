using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Cli;

/// <summary>The file system as the command line uses it by default: writing an object out, writing and deleting a registration.</summary>
internal static class CliFiles
{
    /// <summary>Writes bytes to a file, replacing it.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">The content.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when written.</returns>
    public static Task WriteAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        File.WriteAllBytesAsync(path, bytes.ToArray(), cancellationToken);

    /// <summary>Writes bytes to a file, replacing it and creating its directory first.</summary>
    /// <param name="path">The file, absolute.</param>
    /// <param name="bytes">The content.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when written.</returns>
    public static Task WriteCreatingDirectoryAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);
        return WriteAsync(path, bytes, cancellationToken);
    }

    /// <summary>Deletes a file.</summary>
    /// <param name="path">The file.</param>
    /// <returns><see langword="true"/> when it existed.</returns>
    public static bool Delete(string path)
    {
        if (!File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }
}
