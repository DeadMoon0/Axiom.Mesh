using System;
using System.Buffers;

namespace Axiom.Mesh.Core;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> that keeps nothing and counts what was written into it: how the local port learns
/// the encoded size of an event or a response once, for the queue bounds of LOCAL-SPEC.md §7.4, without holding the bytes.
/// </summary>
internal sealed class CountingBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] scratch = ArrayPool<byte>.Shared.Rent(4096);

    /// <summary>The bytes written so far.</summary>
    public long Count { get; private set; }

    /// <inheritdoc />
    public void Advance(int count) => Count += count;

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0) => Scratch(sizeHint);

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0) => Scratch(sizeHint);

    /// <inheritdoc />
    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(scratch);
        scratch = [];
    }

    private byte[] Scratch(int sizeHint)
    {
        if (sizeHint > scratch.Length)
        {
            ArrayPool<byte>.Shared.Return(scratch);
            scratch = ArrayPool<byte>.Shared.Rent(sizeHint);
        }

        return scratch;
    }
}
