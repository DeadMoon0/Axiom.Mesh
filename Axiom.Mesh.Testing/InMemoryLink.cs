using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Testing;

/// <summary>
/// The dialling end of one in-memory connection, as a cable the <see cref="InMemoryNetwork"/> can cut: a cut fails the
/// reads and writes of this end, as a reset would, and closes the other end, which then reads the end of the stream.
/// </summary>
internal sealed class InMemoryLink : Stream
{
    private readonly Stream inner;
    private readonly CancellationTokenSource cut = new();
    private int disposed;

    /// <summary>A link between two addresses.</summary>
    /// <param name="inner">The dialling end the target node's server handed out.</param>
    /// <param name="from">The dialling side's address.</param>
    /// <param name="to">The target's address.</param>
    public InMemoryLink(Stream inner, IPEndPoint from, IPEndPoint to)
    {
        this.inner = inner;
        From = from;
        To = to;
    }

    /// <summary>The dialling side's address.</summary>
    public IPEndPoint From { get; }

    /// <summary>The target's address.</summary>
    public IPEndPoint To { get; }

    /// <summary>Whether the link was cut or closed.</summary>
    public bool IsClosed => Volatile.Read(ref disposed) != 0 || cut.IsCancellationRequested;

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

    /// <summary>Cuts the cable: this end's pending and later reads and writes fail; the other end reads the end of the stream.</summary>
    public void Cut()
    {
        try
        {
            cut.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        inner.Dispose();
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cut.Token, cancellationToken);
        try
        {
            return await inner.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (cut.IsCancellationRequested && ex is not IOException)
        {
            throw new IOException("The in-memory network was partitioned.", ex);
        }
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cut.IsCancellationRequested)
            throw new IOException("The in-memory network was partitioned.");
        try
        {
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (cut.IsCancellationRequested && ex is not IOException)
        {
            throw new IOException("The in-memory network was partitioned.", ex);
        }
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count).GetAwaiter().GetResult();

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
        if (disposing && Interlocked.Exchange(ref disposed, 1) == 0)
        {
            inner.Dispose();
            cut.Dispose();
        }

        base.Dispose(disposing);
    }
}
