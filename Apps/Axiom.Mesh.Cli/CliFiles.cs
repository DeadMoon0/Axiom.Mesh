using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Cli;

/// <summary>The file system as the command line uses it by default: listing a folder to publish, writing an object out.</summary>
internal static class CliFiles
{
    /// <summary>The regular files under a directory, relative to it, with <c>/</c> between names, in ordinal order.</summary>
    /// <param name="directory">The directory, absolute.</param>
    /// <returns>The relative paths; symbolic links and other special files are left out.</returns>
    public static IReadOnlyList<string> List(string directory)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        return [.. Directory.EnumerateFiles(directory, "*", options)
            .Select(f => Path.GetRelativePath(directory, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Writes bytes to a file, replacing it.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">The content.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when written.</returns>
    public static Task WriteAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        File.WriteAllBytesAsync(path, bytes.ToArray(), cancellationToken);
}
