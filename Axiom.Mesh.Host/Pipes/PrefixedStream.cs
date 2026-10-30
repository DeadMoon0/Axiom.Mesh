using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Host;

/// <summary>
/// A connection whose first bytes were read already: they are read again first, then the connection itself. The
/// Windows listener reads a client's first bytes before it may ask who the client is (a named pipe impersonates only
/// after data was read from it), and hands the bytes on untouched.
/// </summary>
internal sealed class PrefixedStream : Stream
{
    private readonly Stream inner;
    private readonly byte[] prefix;
    private int offset;

    /// <summary>A stream that reads <paramref name="prefix"/> first.</summary>
    /// <param name="prefix">The bytes read already.</param>
    /// <param name="inner">The connection.</param>
    public PrefixedStream(byte[] prefix, Stream inner)
    {
        this.prefix = prefix;
        this.inner = inner;
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (offset < prefix.Length)
            return ValueTask.FromResult(TakePrefix(buffer.Span));
        return inner.ReadAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        this.offset < prefix.Length ? TakePrefix(buffer.AsSpan(offset, count)) : inner.Read(buffer, offset, count);

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override void Flush() => inner.Flush();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private int TakePrefix(Span<byte> destination)
    {
        int count = Math.Min(destination.Length, prefix.Length - offset);
        prefix.AsSpan(offset, count).CopyTo(destination);
        offset += count;
        return count;
    }
}
