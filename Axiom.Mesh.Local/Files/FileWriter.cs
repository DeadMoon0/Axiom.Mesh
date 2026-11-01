using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Local;

/// <summary>
/// Writes one planned file the way SPEC.md §11.4 asks, for <see cref="TreeWriter"/> and
/// <see cref="StoreFiles.WriteFileAsync"/> alike: the object the plan names is read and checked against the file's kind; a
/// file's Chunks go into a hidden temporary name beside the target (<see cref="StoreFiles.PartialPrefix"/>) and are renamed
/// into place only once every Chunk was written and their lengths add up to the ChunkList's size; the executable bit is set
/// where the file system has it; a symlink becomes a regular file holding its target text. Nothing partial is ever visible
/// under the target's name, and a link or a directory at the target is never written through.
/// </summary>
internal static class FileWriter
{
    /// <summary>Writes one file of a plan into a directory.</summary>
    /// <param name="reader">Where objects are read.</param>
    /// <param name="file">The file.</param>
    /// <param name="destination">Its absolute path.</param>
    /// <param name="written">Told the bytes of each Chunk written and the file's path, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels writing; no partial file is left.</param>
    /// <returns>
    /// The bytes written; <c>Invalid</c> when the object is not what the file's kind says, its Chunks do not add up to its
    /// size, or a directory or a link is at the target (nothing written); <c>NotFound</c> for an object not stored here.
    /// </returns>
    /// <exception cref="IOException">The file system refused.</exception>
    public static async Task<LocalResult<ulong>> WriteAsync(ObjectReader reader, PlannedFile file, string destination, Action<ulong, string>? written, CancellationToken cancellationToken)
    {
        if (IsLink(destination) || Directory.Exists(destination))
            return Refused($"{destination} is a directory or a link; a file is not written through it.");
        LocalResult<ParsedObject> named = await reader.ReadAsync(file.Hash, cancellationToken).ConfigureAwait(false);
        if (!named.IsOk)
            return LocalResult<ulong>.Failure(named.Status, named.Error);

        string partial = ChunkCopier.PartialOf(destination);
        try
        {
            ulong bytes;
            switch (named.Value)
            {
                case ChunkList list when file.Kind is PlannedFileKind.File or PlannedFileKind.Executable:
                    LocalResult<bool> copied = await new ChunkCopier(reader, written).CopyAsync(list, partial, file.Path, cancellationToken).ConfigureAwait(false);
                    if (!copied.IsOk)
                        return LocalResult<ulong>.Failure(copied.Status, copied.Error);
                    if (!copied.Value)
                        return Refused($"The Chunks of {file.Hash} do not add up to the {list.Size} bytes it states (SPEC.md §11.4); {file.Path} was not written.");
                    if (file.Kind == PlannedFileKind.Executable && !OperatingSystem.IsWindows())
                        File.SetUnixFileMode(partial, File.GetUnixFileMode(partial) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                    bytes = list.Size;
                    break;
                case Chunk link when file.Kind == PlannedFileKind.Link:
                    await File.WriteAllBytesAsync(partial, link.Data.ToArray(), cancellationToken).ConfigureAwait(false);
                    bytes = (ulong)link.Data.Length;
                    written?.Invoke(bytes, file.Path);
                    break;
                default:
                    return Refused($"{file.Hash} is not what {file.Path} needs ({file.Kind}).");
            }

            File.Move(partial, destination, overwrite: true);
            return LocalResult<ulong>.Ok(bytes);
        }
        finally
        {
            ChunkCopier.Discard(partial);
        }
    }

    /// <summary>Whether a path is a link (a reparse point on Windows), file or directory.</summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> for a link.</returns>
    public static bool IsLink(string path)
    {
        FileSystemInfo? existing = File.Exists(path) ? new FileInfo(path) : Directory.Exists(path) ? new DirectoryInfo(path) : null;
        return existing is not null && (existing.Attributes.HasFlag(FileAttributes.ReparsePoint) || existing.LinkTarget is not null);
    }

    private static LocalResult<ulong> Refused(string message) => LocalResult<ulong>.Failure(Status.Invalid, new Error { Message = message });
}
