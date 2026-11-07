using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>
/// A stream that passes everything through to its inner stream and runs one armed action right before the first byte
/// is written after arming, never again. <see cref="MeshTlsLayer"/> puts it under the server's
/// <see cref="System.Net.Security.SslStream"/>: the first write is the server's first handshake flight, the moment
/// <see cref="OpenSslTickets.IssueNone"/> must run.
/// </summary>
internal sealed class FirstWriteHook : Stream
{
    private readonly Stream inner;
    private Action? beforeFirstWrite;

    /// <summary>Wraps a stream; nothing runs until <see cref="Arm"/>.</summary>
    /// <param name="inner">The stream written and read through; disposed with this one.</param>
    public FirstWriteHook(Stream inner) => this.inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <inheritdoc />
    public override bool CanRead => inner.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => inner.CanWrite;

    /// <inheritdoc />
    public override bool CanTimeout => inner.CanTimeout;

    /// <inheritdoc />
    public override int ReadTimeout { get => inner.ReadTimeout; set => inner.ReadTimeout = value; }

    /// <inheritdoc />
    public override int WriteTimeout { get => inner.WriteTimeout; set => inner.WriteTimeout = value; }

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>Sets what runs before the next write; an exception it throws is that write's.</summary>
    /// <param name="action">The action, run once.</param>
    public void Arm(Action action) => beforeFirstWrite = action ?? throw new ArgumentNullException(nameof(action));

    /// <inheritdoc />
    public override void Flush() => inner.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => inner.Read(buffer);

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        RunHook();
        inner.Write(buffer, offset, count);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        RunHook();
        inner.Write(buffer);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        RunHook();
        return inner.WriteAsync(buffer, offset, count, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        RunHook();
        return inner.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask DisposeAsync() => inner.DisposeAsync();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }

    private void RunHook()
    {
        Action? hook = Interlocked.Exchange(ref beforeFirstWrite, null);
        hook?.Invoke();
    }
}
