using System;
using System.Buffers;
using System.Threading;

namespace Axiom.Mesh.Core;

/// <summary>
/// Object bytes copied out of a frame into a pooled buffer (plan §3.5): parsed objects refer to the memory they were
/// parsed from, so intake copies a frame view once and the store writer gives the buffer back once the file is written.
/// </summary>
internal sealed class PooledCopy : IDisposable
{
    private readonly ArrayPool<byte> pool;
    private readonly int length;
    private byte[]? buffer;

    private PooledCopy(ArrayPool<byte> pool, byte[] buffer, int length)
    {
        this.pool = pool;
        this.buffer = buffer;
        this.length = length;
    }

    /// <summary>The copied bytes; not valid after <see cref="Dispose"/>.</summary>
    public ReadOnlyMemory<byte> Memory => (buffer ?? throw new ObjectDisposedException(nameof(PooledCopy))).AsMemory(0, length);

    /// <summary>
    /// A pool for object copies: buffers up to the largest object, a few per size, shared between the threads that rent
    /// (intake, on a session's thread) and return (the writer) without thread-local caching.
    /// </summary>
    /// <returns>The pool.</returns>
    public static ArrayPool<byte> CreatePool() => ArrayPool<byte>.Create(Limits.MaxObjectLength, 16);

    /// <summary>Copies bytes into a buffer from <paramref name="pool"/>.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="pool">The pool.</param>
    /// <returns>The copy.</returns>
    public static PooledCopy Of(ReadOnlySpan<byte> bytes, ArrayPool<byte> pool)
    {
        byte[] buffer = pool.Rent(Math.Max(1, bytes.Length));
        bytes.CopyTo(buffer);
        return new PooledCopy(pool, buffer, bytes.Length);
    }

    /// <summary>Gives the buffer back to the pool; nothing may use the bytes afterwards.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref buffer, null) is { } returned)
            pool.Return(returned);
    }
}
