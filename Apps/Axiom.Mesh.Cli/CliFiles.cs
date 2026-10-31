using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Cli;

/// <summary>The file system as the command line uses it by default: writing an object out.</summary>
internal static class CliFiles
{
    /// <summary>Writes bytes to a file, replacing it.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">The content.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when written.</returns>
    public static Task WriteAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        File.WriteAllBytesAsync(path, bytes.ToArray(), cancellationToken);
}
