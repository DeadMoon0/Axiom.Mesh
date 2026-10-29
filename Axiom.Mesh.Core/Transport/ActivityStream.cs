using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>
/// The TLS stream as the protocol sees it, recording each read that returned bytes in a <see cref="LinkActivity"/>.
/// Everything else passes through; disposing it disposes the TLS stream and so the connection.
/// </summary>
internal sealed class ActivityStream(Stream inner, LinkActivity activity) : Stream
{
    /// <inheritdoc />
    public override bool CanRead => inner.CanRead;

    /// <inheritdoc />
    public override bool CanWrite => inner.CanWrite;

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
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0)
            activity.Received();
        return read;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = inner.Read(buffer, offset, count);
        if (read > 0)
            activity.Received();
        return read;
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.WriteAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        inner.WriteAsync(buffer, offset, count, cancellationToken);

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
    public override ValueTask DisposeAsync() => inner.DisposeAsync();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}
