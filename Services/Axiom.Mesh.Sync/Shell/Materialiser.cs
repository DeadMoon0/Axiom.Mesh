using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>
/// Carries out a <see cref="DiskPlan"/> in a bound directory (SYNC-SERVICE.md §3 step 11, SPEC.md §11.4): deletes the deepest
/// first (a directory only when empty), creates directories, then writes each file with
/// <see cref="StoreFiles.WriteFileAsync"/> (every Chunk held, the size checked, a hidden temporary name renamed into place), a
/// few at once, and records the fingerprints of each batch right after it, so a resumed run finds them in place and writes only
/// what is left.
/// </summary>
internal sealed class Materialiser
{
    private const int FingerprintBatch = 256;
    private const int FilesAtOnce = 8;

    private readonly ObjectReader reader;
    private readonly SyncStore store;
    private readonly BindingSpec spec;
    private readonly Func<RunStep, bool> crashAt;
    private readonly List<Fingerprint> written = [];
    private readonly List<string> deleted = [];

    /// <summary>A materialiser for one run.</summary>
    /// <param name="reader">Where objects are read.</param>
    /// <param name="store">Where fingerprints are recorded.</param>
    /// <param name="spec">The binding.</param>
    /// <param name="crashAt">Where a test stops the run.</param>
    public Materialiser(ObjectReader reader, SyncStore store, BindingSpec spec, Func<RunStep, bool> crashAt)
    {
        this.reader = reader;
        this.store = store;
        this.spec = spec;
        this.crashAt = crashAt;
    }

    /// <summary>Carries out a plan.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="cancellationToken">Cancels the writes; the file in progress leaves nothing behind.</param>
    /// <returns>Ok; <c>NotFound</c> when an object is not stored (the run waits for it); <c>Invalid</c> paths come back as conflicts.</returns>
    /// <exception cref="IOException">The file system refused.</exception>
    public async Task<(Status Status, IReadOnlyList<string> Faulty)> RunAsync(DiskPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var faulty = new List<string>();
        foreach (string path in plan.Deletes)
            Delete(path);
        foreach (string path in plan.Directories)
            Directory.CreateDirectory(Full(path));
        await FlushAsync(cancellationToken).ConfigureAwait(false);

        // The first file alone, so a test can stop the run right after it as a crash would; then a batch at a time, a few
        // files of it at once, each batch's fingerprints recorded before the next.
        KeyValuePair<string, FlatEntry>[] writes = [.. plan.Writes];
        for (int at = 0; at < writes.Length;)
        {
            int count = at == 0 ? 1 : Math.Min(FingerprintBatch, writes.Length - at);
            ArraySegment<KeyValuePair<string, FlatEntry>> batch = new(writes, at, count);
            LocalResult<ulong>[] results = new LocalResult<ulong>[count];
            await Parallel.ForEachAsync(Enumerable.Range(0, count), new ParallelOptions { MaxDegreeOfParallelism = FilesAtOnce, CancellationToken = cancellationToken }, async (i, ct) =>
                results[i] = await WriteAsync(batch[i].Key, batch[i].Value, ct).ConfigureAwait(false)).ConfigureAwait(false);
            LocalResult<ulong>? failed = null;
            for (int i = 0; i < count; i++)
            {
                (string path, FlatEntry entry) = batch[i];
                if (results[i].Status == Status.Invalid)
                    faulty.Add(path);
                else if (!results[i].IsOk)
                    failed ??= results[i];
                else
                    Written(path, entry);
            }

            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
            if (failed is { } failure)
            {
                if (failure.Status == Status.IoError)
                    throw new IOException(failure.Error?.Message ?? "A file cannot be written.");
                return (failure.Status, faulty);
            }

            if (at == 0 && crashAt(RunStep.FileWritten))
                throw new RunStoppedException(RunStep.FileWritten);
            at += count;
        }

        await FlushAsync(cancellationToken).ConfigureAwait(false);
        return (Status.Ok, faulty);
    }

    private Task<LocalResult<ulong>> WriteAsync(string path, FlatEntry entry, CancellationToken cancellationToken) =>
        StoreFiles.WriteFileAsync(reader, new PlannedFile(path, entry.Hash, KindOf(entry.Mode), entry.Size), spec.Directory, cancellationToken);

    // A file written: its fingerprint takes what the file system now says, and the largest taken time (never racy).
    private void Written(string path, FlatEntry entry)
    {
        var info = new FileInfo(Full(path));
        written.Add(new Fingerprint(path, entry.Mode, (ulong)info.Length, info.LastWriteTimeUtc.Ticks, entry.Hash, long.MaxValue));
    }

    private void Delete(string path)
    {
        string full = Full(path);
        if (File.Exists(full))
        {
            File.Delete(full);
            deleted.Add(path);
            return;
        }

        if (!Directory.Exists(full))
        {
            deleted.Add(path);
            return;
        }

        try
        {
            Directory.Delete(full, recursive: false);
        }
        catch (IOException)
        {
            // Something new lies in it: the directory stays.
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (written.Count == 0 && deleted.Count == 0)
            return;
        await store.WriteFingerprintsAsync(spec.Id, [.. written], [.. deleted], LDB.Durability.Lazy, cancellationToken).ConfigureAwait(false);
        written.Clear();
        deleted.Clear();
    }

    private string Full(string path) => Path.Combine(spec.Directory, path.Replace('/', Path.DirectorySeparatorChar));

    private static PlannedFileKind KindOf(TreeEntryMode mode) => mode switch
    {
        TreeEntryMode.ExecutableFile => PlannedFileKind.Executable,
        TreeEntryMode.Symlink => PlannedFileKind.Link,
        _ => PlannedFileKind.File,
    };
}
