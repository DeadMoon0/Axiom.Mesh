using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Local;

/// <summary>
/// Writes one ChunkList to a file the way SPEC.md §11.4 asks, for <see cref="TreeWriter"/> and
/// <see cref="StoreFiles.MaterialiseFileAsync"/> alike: the Chunks in order into a hidden temporary name beside the target
/// (<see cref="StoreFiles.PartialPrefix"/>), flushed, and renamed into place only once every Chunk was written and their
/// lengths add up to the ChunkList's size. A cancelled or failed copy removes the temporary file, so nothing partial is
/// ever visible under the target's name.
/// </summary>
internal sealed class ChunkCopier
{
    private readonly ObjectReader reader;
    private readonly Action<ulong, string>? written;

    /// <summary>A copier.</summary>
    /// <param name="reader">Where the Chunks are read.</param>
    /// <param name="written">Told the bytes of each Chunk written and the name being written, or <see langword="null"/>.</param>
    public ChunkCopier(ObjectReader reader, Action<ulong, string>? written)
    {
        this.reader = reader;
        this.written = written;
    }

    /// <summary>The temporary name a file is written to before it is renamed to <paramref name="destination"/>.</summary>
    /// <param name="destination">The target path, absolute.</param>
    /// <returns>The partial path beside it.</returns>
    public static string PartialOf(string destination) =>
        Path.Combine(Path.GetDirectoryName(destination)!, StoreFiles.PartialPrefix + Path.GetFileName(destination));

    /// <summary>Copies a ChunkList's Chunks into <paramref name="partial"/>, without renaming it.</summary>
    /// <param name="list">The ChunkList.</param>
    /// <param name="partial">The temporary file, created or truncated.</param>
    /// <param name="name">The name reported with progress.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>
    /// <see langword="true"/> when every Chunk was written and the size holds; <see langword="false"/> when an object named
    /// is no Chunk or the size does not hold (a conflict); a failure when a Chunk is not stored.
    /// </returns>
    /// <exception cref="IOException">The file system refused.</exception>
    public async Task<LocalResult<bool>> CopyAsync(ChunkList list, string partial, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        ulong total = 0;
        await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 0, useAsync: true);
        foreach (ObjectHash hash in list.Chunks)
        {
            LocalResult<ParsedObject> read = await reader.ReadAsync(hash, cancellationToken).ConfigureAwait(false);
            if (!read.IsOk)
                return LocalResult<bool>.Failure(read.Status, read.Error);
            if (read.Value is not Chunk chunk)
                return LocalResult<bool>.Ok(false);

            await output.WriteAsync(chunk.Data, cancellationToken).ConfigureAwait(false);
            total += (ulong)chunk.Data.Length;
            written?.Invoke((ulong)chunk.Data.Length, name);
        }

        // SPEC.md §11.4: the Chunks must add up to the size the ChunkList states.
        if (total != list.Size)
            return LocalResult<bool>.Ok(false);
        output.Flush(flushToDisk: true);
        return LocalResult<bool>.Ok(true);
    }

    /// <summary>Removes a temporary file a copy left, if any.</summary>
    /// <param name="partial">The temporary file.</param>
    public static void Discard(string partial)
    {
        if (File.Exists(partial))
            File.Delete(partial);
    }
}
