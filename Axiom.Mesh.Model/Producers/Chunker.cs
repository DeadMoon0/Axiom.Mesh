using System;
using System.Security.Cryptography;

namespace Axiom.Mesh;

/// <summary>
/// Cuts file contents into Chunks of exactly 1 MiB except the last, which holds the remainder; an empty file has no
/// Chunks (SPEC.md §8.2). <see cref="Cut"/> is for contents already in memory; a streaming reader reads each part of a
/// file straight into a reused buffer behind <see cref="HeaderLength"/> bytes and finishes it with <see cref="Seal"/>,
/// which allocates nothing.
/// </summary>
public static class Chunker
{
    /// <summary>The bytes a Chunk object starts with before its data: the tag and the version (SPEC.md §8.1).</summary>
    public const int HeaderLength = 2;

    /// <summary>The longest Chunk object: its header and 1 MiB of data; the size of a buffer <see cref="Seal"/> takes.</summary>
    public const int MaxChunkObjectLength = HeaderLength + Limits.ChunkLength;

    /// <summary>Cuts <paramref name="content"/> and builds its ChunkList.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <returns>The Chunks and the ChunkList.</returns>
    public static ChunkedFile Cut(ReadOnlyMemory<byte> content)
    {
        int count = (int)(((long)content.Length + Limits.ChunkLength - 1) / Limits.ChunkLength);
        var chunks = new Chunk[count];
        var hashes = new ObjectHash[count];
        for (int i = 0; i < count; i++)
        {
            int start = i * Limits.ChunkLength;
            chunks[i] = ObjectBuilders.Chunk(content.Span.Slice(start, Math.Min(Limits.ChunkLength, content.Length - start)));
            hashes[i] = chunks[i].Hash;
        }

        return new ChunkedFile(ObjectBuilders.ChunkList((ulong)content.Length, hashes), chunks);
    }

    /// <summary>
    /// Makes a Chunk object in place (SPEC.md §8.2): <paramref name="chunk"/> holds the data from offset
    /// <see cref="HeaderLength"/> on; the tag and version are written before it and the whole object is hashed. Nothing is
    /// allocated or copied, so a reader that cuts a file reuses one buffer per Chunk in flight.
    /// </summary>
    /// <param name="chunk">The object: <see cref="HeaderLength"/> bytes this method writes, then 1 byte to 1 MiB of data.</param>
    /// <returns>The Chunk's hash, <c>H(object)</c>.</returns>
    /// <exception cref="ArgumentException">The data is empty or longer than 1 MiB.</exception>
    public static ObjectHash Seal(Span<byte> chunk)
    {
        int data = chunk.Length - HeaderLength;
        if (data is < Limits.MinChunkLength or > Limits.ChunkLength)
            throw new ArgumentException($"A Chunk holds 1 to {Limits.ChunkLength} bytes after its {HeaderLength}-byte header; {Math.Max(0, data)} were given (SPEC.md §8.2).", nameof(chunk));

        chunk[0] = (byte)ObjectTag.Chunk;
        chunk[1] = Objects.Version;
        Span<byte> hash = stackalloc byte[Hash32.Length];
        SHA256.HashData(chunk, hash);
        return new ObjectHash(new Hash32(hash));
    }
}
