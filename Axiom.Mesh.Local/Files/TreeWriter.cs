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
            LocalResult<ulong> written = await FileWriter.WriteAsync(reader, file, PathOf(target, file.Path), Written, cancellationToken).ConfigureAwait(false);
            if (written.IsOk)
                files++;
            else if (written.Status == Status.Invalid)
                conflicts.Add(file.Path);
            else
                return LocalResult<MaterialisedTree>.Failure(written.Status, written.Error);
        }

        return LocalResult<MaterialisedTree>.Ok(new MaterialisedTree { Tree = tree, Files = files, Bytes = done, Conflicts = conflicts });
    }

    private void Written(ulong bytes, string name)
    {
        done += bytes;
        progress?.Report(new MaterialiseProgress(done, total, name));
    }

    private static string PathOf(string target, string relative) => Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
}
