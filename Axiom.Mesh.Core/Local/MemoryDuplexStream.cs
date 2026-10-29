using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>
/// One end of an in-memory duplex connection over two <see cref="Pipe"/>s: what one end writes the other reads, and
/// disposing an end lets the other read the end of the stream. The transport of an in-process service (LOCAL-SPEC.md
/// §4.2, DESIGN.md §10.3).
/// </summary>
internal sealed class MemoryDuplexStream : Stream
{
    private readonly Stream input;
    private readonly Stream output;

    private MemoryDuplexStream(PipeReader reader, PipeWriter writer)
    {
        input = reader.AsStream();
        output = writer.AsStream();
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

    /// <summary>A connected pair.</summary>
    /// <returns>One end, and the other.</returns>
    public static (Stream One, Stream Other) CreatePair()
    {
        var oneToOther = new Pipe(new PipeOptions(useSynchronizationContext: false));
        var otherToOne = new Pipe(new PipeOptions(useSynchronizationContext: false));
        return (new MemoryDuplexStream(otherToOne.Reader, oneToOther.Writer), new MemoryDuplexStream(oneToOther.Reader, otherToOne.Writer));
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => input.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => input.ReadAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await output.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override void Flush() => output.Flush();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            input.Dispose();
            output.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await input.DisposeAsync().ConfigureAwait(false);
        await output.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
