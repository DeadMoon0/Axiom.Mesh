using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// The object files of one batch, written before its transaction begins and several at a time (SPEC.md §11.1: temporary
/// file, flush, rename). A flush per file is the store's dearest step; one after the other they bounded a history walk
/// to one Entry per flush (STRUCTURAL-DEBT.md 52). The batch then decides per object as before; a write that failed fails
/// only its own command, and a file written now for an object the batch did not store is deleted again.
/// </summary>
internal sealed class FileWrites
{
    /// <summary>How many files are written at once.</summary>
    public const int Parallel = 8;

    private readonly ConcurrentDictionary<ObjectHash, Exception?> failed = new();
    private readonly ConcurrentDictionary<ObjectHash, bool> writtenNow = new();

    private FileWrites()
    {
    }

    /// <summary>Writes the file of every object a batch admits, <see cref="Parallel"/> at a time.</summary>
    /// <param name="files">The object files.</param>
    /// <param name="batch">The batch's commands; only <see cref="AdmitCommand"/>s carry objects.</param>
    /// <returns>What was written, and what failed.</returns>
    public static async Task<FileWrites> RunAsync(IObjectFiles files, IReadOnlyList<StoreCommand> batch)
    {
        StorableObject[] objects = [.. batch.OfType<AdmitCommand>().Select(c => c.Object).DistinctBy(o => o.Hash)];
        var writes = new FileWrites();
        if (objects.Length == 0)
            return writes;
        await System.Threading.Tasks.Parallel.ForEachAsync(objects, new ParallelOptions { MaxDegreeOfParallelism = Parallel }, async (storable, _) =>
        {
            try
            {
                writes.writtenNow[storable.Hash] = await files.WriteAtomicAsync(storable.Hash, storable.Bytes, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                writes.failed[storable.Hash] = e;
            }
        }).ConfigureAwait(false);
        return writes;
    }

    /// <summary>Whether an object's file was written ahead, and how that went.</summary>
    /// <param name="hash">The object.</param>
    /// <param name="error">The failure, or <see langword="null"/> when the file is in place.</param>
    /// <returns><see langword="true"/> when the object was written ahead (or its write failed).</returns>
    public bool TryGet(ObjectHash hash, out Exception? error)
    {
        if (failed.TryGetValue(hash, out error))
            return true;
        error = null;
        return writtenNow.ContainsKey(hash);
    }

    /// <summary>The files this batch created (not those already present).</summary>
    public IEnumerable<ObjectHash> Created => writtenNow.Where(w => w.Value).Select(w => w.Key);
}
