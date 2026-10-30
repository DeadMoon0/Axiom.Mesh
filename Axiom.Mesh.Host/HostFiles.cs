using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Host;

/// <summary>
/// Files only the daemon's user may read and write (LOCAL-SPEC.md §13): on Linux directories are made 0700 and files
/// 0600; on Windows they live under <c>%LOCALAPPDATA%</c>, whose access list admits the user alone and is inherited.
/// </summary>
internal static class HostFiles
{
    /// <summary>The mode of a private directory on Unix: rwx for the owner only.</summary>
    public const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>The mode of a private file on Unix: rw for the owner only.</summary>
    public const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Creates a directory that only the user can enter; an existing one is left as it is.</summary>
    /// <param name="path">The directory.</param>
    public static void CreatePrivateDirectory(string path)
    {
        if (Directory.Exists(path))
            return;
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, PrivateDirectory);
    }

    /// <summary>Writes a file that only the user can read, replacing what it held.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">Its new content.</param>
    /// <param name="cancellationToken">Cancels writing.</param>
    /// <returns>A task that completes when the bytes are flushed to the disk.</returns>
    public static async Task WritePrivateAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.WriteThrough };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = PrivateFile;
        FileStream stream = new(path, options);
        await using (stream.ConfigureAwait(false))
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, PrivateFile);
    }

    /// <summary>Whether a file or directory on Unix grants anything to its group or to others.</summary>
    /// <param name="mode">Its mode.</param>
    /// <returns><see langword="true"/> when it is wider than the owner's.</returns>
    public static bool IsWiderThanOwner(UnixFileMode mode) =>
        (mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0;
}
