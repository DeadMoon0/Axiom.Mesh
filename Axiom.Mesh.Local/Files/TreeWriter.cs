using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Local;

/// <summary>
/// Carries out a <see cref="MaterialisePlan"/> (SPEC.md §11.4): every file written to a hidden temporary name beside it
/// (<see cref="StoreFiles.PartialPrefix"/>) and renamed into place only once every Chunk was written and the ChunkList's
/// size holds (<see cref="ChunkCopier"/>), so a cancelled or failed write leaves no partial file visible; executable mode
/// where the file system has it; a symlink as a regular file holding its target. The file system shell of
/// <see cref="StoreFiles.MaterialiseAsync"/>.
/// </summary>
internal sealed class TreeWriter
{
    private readonly ObjectReader reader;
    private readonly IProgress<MaterialiseProgress>? progress;
    private readonly ulong total;
    private readonly List<string> conflicts;
    private readonly ChunkCopier copier;
    private ulong done;
    private int files;

    /// <summary>A writer for one plan.</summary>
    /// <param name="reader">Where objects are read.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="progress">Where progress goes, or <see langword="null"/>.</param>
    public TreeWriter(ObjectReader reader, MaterialisePlan plan, IProgress<MaterialiseProgress>? progress)
    {
        this.reader = reader;
        this.progress = progress;
        total = plan.TotalBytes;
        conflicts = [.. plan.Conflicts];
        copier = new ChunkCopier(reader, Written);
    }

    /// <summary>Writes the plan's directories and files into <paramref name="target"/>, which exists.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="target">The directory, absolute.</param>
    /// <param name="tree">The Tree written, for the result.</param>
    /// <param name="cancellationToken">Cancels writing; the file in progress is removed.</param>
    /// <returns>What was written, or why the work stopped (an object not stored).</returns>
    /// <exception cref="IOException">The file system refused.</exception>
    public async Task<LocalResult<MaterialisedTree>> WriteAsync(MaterialisePlan plan, string target, ObjectHash tree, CancellationToken cancellationToken)
    {
        foreach (string directory in plan.Directories)
            Directory.CreateDirectory(PathOf(target, directory));

        foreach (PlannedFile file in plan.Files)
        {
            string destination = PathOf(target, file.Path);
            if (IsLink(destination) || Directory.Exists(destination))
            {
                conflicts.Add(file.Path);
                continue;
            }

            LocalResult<bool> written = await WriteFileAsync(file, destination, cancellationToken).ConfigureAwait(false);
            if (!written.IsOk)
                return LocalResult<MaterialisedTree>.Failure(written.Status, written.Error);
            if (written.Value)
                files++;
            else
                conflicts.Add(file.Path);
        }

        return LocalResult<MaterialisedTree>.Ok(new MaterialisedTree { Tree = tree, Files = files, Bytes = done, Conflicts = conflicts });
    }

    // A file or link: true when written, false for a conflict (the object is not what the mode says, or the size is off).
    private async Task<LocalResult<bool>> WriteFileAsync(PlannedFile file, string destination, CancellationToken cancellationToken)
    {
        LocalResult<ParsedObject> named = await reader.ReadAsync(file.Hash, cancellationToken).ConfigureAwait(false);
        if (!named.IsOk)
            return LocalResult<bool>.Failure(named.Status, named.Error);

        string partial = ChunkCopier.PartialOf(destination);
        try
        {
            switch (named.Value)
            {
                case ChunkList list when file.Kind is PlannedFileKind.File or PlannedFileKind.Executable:
                    LocalResult<bool> copied = await copier.CopyAsync(list, partial, file.Path, cancellationToken).ConfigureAwait(false);
                    if (!copied.IsOk || !copied.Value)
                        return copied;
                    if (file.Kind == PlannedFileKind.Executable && !OperatingSystem.IsWindows())
                        File.SetUnixFileMode(partial, File.GetUnixFileMode(partial) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                    break;
                case Chunk link when file.Kind == PlannedFileKind.Link:
                    await File.WriteAllBytesAsync(partial, link.Data.ToArray(), cancellationToken).ConfigureAwait(false);
                    done += (ulong)link.Data.Length;
                    break;
                default:
                    return LocalResult<bool>.Ok(false);
            }

            File.Move(partial, destination, overwrite: true);
            return LocalResult<bool>.Ok(true);
        }
        finally
        {
            ChunkCopier.Discard(partial);
        }
    }

    private void Written(ulong bytes, string name)
    {
        done += bytes;
        progress?.Report(new MaterialiseProgress(done, total, name));
    }

    private static string PathOf(string target, string relative) => Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));

    private static bool IsLink(string path)
    {
        FileSystemInfo? existing = File.Exists(path) ? new FileInfo(path) : Directory.Exists(path) ? new DirectoryInfo(path) : null;
        return existing is not null && existing.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }
}
