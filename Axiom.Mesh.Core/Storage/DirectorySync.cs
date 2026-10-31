using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// Flushes a directory's entries to disk: on POSIX platforms <c>open(O_RDONLY)</c> + <c>fsync</c> + <c>close</c> through
/// libc, because .NET refuses to open a directory as a file (STRUCTURAL-DEBT.md 14). Windows needs no directory flush.
/// The one place the store calls the C library.
/// </summary>
internal static class DirectorySync
{
    // O_RDONLY is 0 on every POSIX platform .NET runs on; a directory opened read-only may be fsynced.
    private const int ReadOnly = 0;

    /// <summary>Flushes a directory.</summary>
    /// <param name="directory">The directory, absolute.</param>
    /// <returns>What happened; never throws.</returns>
    public static StoreDirectoryFlush Flush(string directory)
    {
        if (OperatingSystem.IsWindows())
            return StoreDirectoryFlush.NotNeeded;

        try
        {
            int descriptor = Open(Encoding.UTF8.GetBytes(directory + "\0"), ReadOnly);
            if (descriptor < 0)
                return StoreDirectoryFlush.Unsupported;
            try
            {
                return Fsync(descriptor) == 0 ? StoreDirectoryFlush.Flushed : StoreDirectoryFlush.Unsupported;
            }
            finally
            {
                _ = Close(descriptor);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return StoreDirectoryFlush.Unsupported;
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(byte[] path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);
}
