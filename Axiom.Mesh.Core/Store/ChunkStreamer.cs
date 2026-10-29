using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>
/// The streaming twin of Model's <see cref="Chunker"/>: cuts a file into Chunks of exactly 1 MiB except the last
/// (SPEC.md §8.2) while reading it, one pooled buffer at a time, and builds the ChunkList (§8.3). A file of any size up
/// to what a ChunkList can name passes through 1 MiB of memory.
/// </summary>
internal static class ChunkStreamer
{
    private const int HeaderLength = 2;

    /// <summary>
    /// Reads <paramref name="source"/> to its end, hands each Chunk to <paramref name="sink"/> before reading the next,
    /// and returns the ChunkList. The Chunk lives in a buffer that is reused once the sink's task completes, so the sink
    /// must be done with it by then (storing it is).
    /// </summary>
    /// <param name="source">The file's contents, read from the current position.</param>
    /// <param name="sink">What to do with each Chunk, in file order.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The ChunkList: the file's size and its Chunks' hashes; no Chunks for an empty file.</returns>
    /// <exception cref="ArgumentException">The file needs more Chunks than a ChunkList can name.</exception>
    /// <exception cref="IOException">A seekable file's length changed while it was read.</exception>
    public static async Task<ChunkList> CutAsync(Stream source, Func<Chunk, CancellationToken, ValueTask> sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sink);
        long? expected = source.CanSeek ? source.Length - source.Position : null;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(HeaderLength + Limits.ChunkLength);
        var hashes = new List<ObjectHash>();
        long size = 0;
        try
        {
            buffer[0] = (byte)ObjectTag.Chunk;
            buffer[1] = Objects.Version;
            while (true)
            {
                int read = await source.ReadAtLeastAsync(buffer.AsMemory(HeaderLength, Limits.ChunkLength), Limits.ChunkLength, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (hashes.Count == Limits.MaxChunkListChunks)
                    throw new ArgumentException($"The file is larger than a ChunkList can name ({Limits.MaxChunkListChunks} Chunks of {Limits.ChunkLength} bytes, SPEC.md §8.3).", nameof(source));

                Chunk chunk = Parse(buffer.AsMemory(0, HeaderLength + read));
                await sink(chunk, cancellationToken).ConfigureAwait(false);
                hashes.Add(chunk.Hash);
                size += read;
                if (read < Limits.ChunkLength)
                    break;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (expected is { } length && length != size)
            throw new IOException($"The file changed while it was read: {length} bytes were expected, {size} were read.");
        return ObjectBuilders.ChunkList((ulong)size, hashes);
    }

    private static Chunk Parse(ReadOnlyMemory<byte> bytes) =>
        Objects.TryParse(HashedObject.Of(bytes), out ParsedObject? parsed, out ObjectFault fault) && parsed is Chunk chunk
            ? chunk
            : throw new InvalidOperationException("A Chunk built from 1 byte to 1 MiB of data did not parse: " + fault);
}
