using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Integration.Tests.Support;

/// <summary>
/// A stream under a client <see cref="System.Net.Security.SslStream"/> that hands it one TLS record per read and counts the
/// records it has started to serve. Read through it, the stream can never pull a later record along with an earlier one,
/// so the count of records served between the end of the handshake and the first application byte is exactly the
/// post-handshake messages the server sent before that byte, plus one: a <c>NewSessionTicket</c> is such a message.
/// </summary>
internal sealed class TlsRecordFeed : Stream
{
    private const int Header = 5;
    private readonly Stream inner;
    private readonly byte[] buffer = new byte[Header + 16384 + 256];
    private int filled;
    private int served;
    private int recordEnd;

    /// <summary>Feeds records from a stream.</summary>
    /// <param name="inner">The connection; disposed with this stream.</param>
    public TlsRecordFeed(Stream inner) => this.inner = inner;

    /// <summary>How many records this stream has started to serve.</summary>
    public int Records { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (destination.IsEmpty)
            return 0;
        if (served == recordEnd)
        {
            if (!await FillAsync(Header, cancellationToken))
                return 0;
            int length = (buffer[3] << 8) | buffer[4];
            if (!await FillAsync(Header + length, cancellationToken))
                throw new EndOfStreamException($"The connection ended inside a record of {length} bytes.");
            recordEnd = Header + length;
            Records++;
        }

        int count = Math.Min(destination.Length, recordEnd - served);
        buffer.AsMemory(served, count).CopyTo(destination);
        served += count;
        if (served == recordEnd)
        {
            Array.Copy(buffer, recordEnd, buffer, 0, filled - recordEnd);
            filled -= recordEnd;
            served = recordEnd = 0;
        }

        return count;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default) => inner.WriteAsync(source, cancellationToken);

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc />
    public override void Flush() => inner.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

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

    // Whether at least `needed` bytes are buffered, reading more while the connection is open.
    private async ValueTask<bool> FillAsync(int needed, CancellationToken cancellationToken)
    {
        if (needed > buffer.Length)
            throw new InvalidDataException($"A record of {needed - Header} bytes is longer than TLS 1.3 allows.");
        while (filled < needed)
        {
            int read = await inner.ReadAsync(buffer.AsMemory(filled), cancellationToken);
            if (read == 0)
                return false;
            filled += read;
        }

        return true;
    }
}
