using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>
/// Reads the changed files of a run into the store (SYNC-SERVICE.md §3 step 4) with <see cref="StoreFiles.PutFileAsync(LocalClient, string, int, IProgress{long}, CancellationToken)"/>:
/// files at once within a budget of requests in flight, a small file taking one and a large one a few (its Chunks in flight),
/// so a run never holds more than 16 requests of the connection's 64. Small files dominate a first sync:
/// the Host shares one batch among concurrent puts, so their number in flight is what the throughput rests on. A file's fingerprint takes the size and write time the scan saw, before the read: a write during the read
/// makes the helper fail, and the file is read again at a later run.
/// </summary>
internal static class FileHasher
{
    private const int InFlight = 16;
    private const int ChunksOfALargeFile = 4;

    /// <summary>Reads files.</summary>
    /// <param name="client">The service's connection.</param>
    /// <param name="directory">The bound directory.</param>
    /// <param name="files">The files to read, with the mode each publishes with.</param>
    /// <param name="takenTicks">When the scan saw them, in UTC ticks: the fingerprints remember it.</param>
    /// <param name="cancellationToken">Cancels the reads; what was put stays pinned until a run releases it.</param>
    /// <returns>What the reads gave.</returns>
    public static async Task<HashedFiles> HashAsync(LocalClient client, string directory, IReadOnlyList<ScannedFile> files, long takenTicks, CancellationToken cancellationToken)
    {
        var prints = new ConcurrentBag<Fingerprint>();
        var problems = new ConcurrentBag<ScanProblem>();
        var puts = new ConcurrentBag<ObjectHash>();
        var vanished = new ConcurrentBag<string>();
        var busy = new ConcurrentBag<string>();
        using var slots = new SemaphoreSlim(InFlight, InFlight);
        var reads = new List<Task>(files.Count);
        foreach (ScannedFile file in files)
        {
            // Only this loop takes slots, one at a time, so taking several for a large file cannot deadlock.
            int chunks = file.Size > (ulong)Limits.ChunkLength ? ChunksOfALargeFile : 1;
            for (int i = 0; i < chunks; i++)
                await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            reads.Add(ReadOneAsync(file, chunks));
        }

        await Task.WhenAll(reads).ConfigureAwait(false);
        return new HashedFiles([.. prints.OrderBy(p => p.Path, StringComparer.Ordinal)], [.. problems], [.. puts], [.. vanished], [.. busy]);

        async Task ReadOneAsync(ScannedFile file, int chunks)
        {
            try
            {
                string path = Path.Combine(directory, file.Path.Replace('/', Path.DirectorySeparatorChar));
                LocalResult<StoredFile> stored = await StoreFiles.PutFileAsync(client, path, chunks, null, cancellationToken).ConfigureAwait(false);
                if (stored.IsOk)
                {
                    prints.Add(new Fingerprint(file.Path, file.Mode, stored.Value.Size, file.WriteTicks, stored.Value.ChunkList, takenTicks));
                    puts.Add(stored.Value.ChunkList);
                    foreach (ObjectHash chunk in stored.Value.Chunks)
                        puts.Add(chunk);
                    return;
                }

                switch (stored.Status)
                {
                    case Status.NotFound:
                        vanished.Add(file.Path);
                        break;
                    case Status.Invalid:
                        problems.Add(new ScanProblem(file.Path, ConflictKind.TooLarge));
                        break;
                    case Status.IoError when File.Exists(path) && new FileInfo(path).LastWriteTimeUtc.Ticks != file.WriteTicks:
                        busy.Add(file.Path);
                        break;
                    case Status.IoError:
                        problems.Add(new ScanProblem(file.Path, ConflictKind.Unreadable));
                        break;
                    default:
                        throw new IOException($"The Host refused a part of {file.Path}: {stored.Status} {stored.Error?.Message}");
                }
            }
            finally
            {
                slots.Release(chunks);
            }
        }
    }
}
