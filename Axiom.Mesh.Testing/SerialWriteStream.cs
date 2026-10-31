using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Testing;

/// <summary>
/// A stream that lets one write at a time through to the stream under it (STRUCTURAL-DEBT.md 50): an <c>SslStream</c>
/// refuses a second write while one is in progress, and the scripted peer writes both through its AMP/1 session and raw
/// (a frame begun and not ended). Both go through this stream and take its one lock; reads pass through unlocked.
/// </summary>
internal sealed class SerialWriteStream : Stream
{
    private readonly Stream inner;
    private readonly SemaphoreSlim writing = new(1, 1);

    /// <summary>Serialises the writes to <paramref name="inner"/>, which it owns.</summary>
    /// <param name="inner">The stream written to.</param>
    public SerialWriteStream(Stream inner) => this.inner = inner;

    /// <inheritdoc />
    public override bool CanRead => inner.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => inner.CanWrite;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Writes and flushes as one step, no other write between them.</summary>
    /// <param name="buffer">The bytes.</param>
    /// <param name="cancellationToken">Cancels waiting for the lock and writing.</param>
    /// <returns>A task that completes when the bytes were flushed.</returns>
    public async Task WriteAndFlushAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writing.Release();
        }
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writing.Release();
        }
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writing.Release();
        }
    }

    /// <inheritdoc />
    public override void Flush() => FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        writing.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            writing.Dispose();
        }

        base.Dispose(disposing);
    }
}
