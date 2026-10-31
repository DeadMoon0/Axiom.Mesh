using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Codecs;
using Axiom.ProtoStream.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Axiom.Mesh.Protocol.Tests.Support;

/// <summary>
/// One AMP/1 session under test on a fake clock, and the other end of its in-memory transport as raw bytes: the test
/// writes frames as any peer could and decodes what the session sent.
/// </summary>
internal sealed class RawPeer : IAsyncDisposable
{
    // A deadline for a frame that should arrive, so a broken test fails instead of hanging; never a wait for time to pass.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private readonly IDuplexPipe peer;
    private readonly CodecRunner<MeshMessage, MeshMessage> decoder = new(Amp.Definition);

    private RawPeer(FakeTimeProvider time, Connection connection, Session<MeshMessage, MeshMessage> session, IDuplexPipe peer)
    {
        Time = time;
        Connection = connection;
        Session = session;
        this.peer = peer;
    }

    /// <summary>The session's clock; advancing it fires the session's timers.</summary>
    public FakeTimeProvider Time { get; }

    /// <summary>The connection the session runs on.</summary>
    public Connection Connection { get; }

    /// <summary>The session under test.</summary>
    public Session<MeshMessage, MeshMessage> Session { get; }

    /// <summary>Opens a session of <see cref="Amp.Definition"/> at a fixed time.</summary>
    public static async Task<RawPeer> OpenAsync()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        TransportPair pair = InMemoryTransport.CreatePair();
        Connection connection = Connection.FromPipe(pair.Client, new ConnectionOptions { TimeProvider = time });
        Session<MeshMessage, MeshMessage> session = await connection.OpenAsync(Amp.Definition, CancellationToken.None);
        return new RawPeer(time, connection, session, pair.Server);
    }

    /// <summary>Writes a frame as the peer.</summary>
    public async Task SendAsync(byte[] frame)
    {
        await peer.Output.WriteAsync(frame);
        await peer.Output.FlushAsync();
    }

    /// <summary>Writes a message as the peer.</summary>
    public Task SendAsync(MeshMessage message) => SendAsync(Frames.Encode(message));

    /// <summary>The next message the session sent, or <see langword="null"/> when it closed its side.</summary>
    public async Task<MeshMessage?> ReceiveAsync()
    {
        using var deadline = new CancellationTokenSource(Deadline);
        while (true)
        {
            ReadResult read = await peer.Input.ReadAsync(deadline.Token);
            DecodeOutcome<MeshMessage> outcome = decoder.Decode(read.Buffer, read.IsCompleted);
            if (outcome.Status == DecodeStatus.Message)
            {
                peer.Input.AdvanceTo(outcome.Consumed);
                return outcome.Message;
            }

            Assert.True(outcome.Status == DecodeStatus.NeedMore, $"The session sent an undecodable frame: {outcome.Detail}");
            if (read.IsCompleted)
            {
                peer.Input.AdvanceTo(read.Buffer.End);
                return null;
            }

            peer.Input.AdvanceTo(read.Buffer.Start, read.Buffer.End);
        }
    }

    /// <summary>Whether the session has sent bytes the peer has not read yet.</summary>
    public bool HasPendingInput()
    {
        if (!peer.Input.TryRead(out ReadResult read))
            return false;
        bool pending = !read.Buffer.IsEmpty;
        peer.Input.AdvanceTo(read.Buffer.Start, read.Buffer.Start);
        return pending;
    }

    /// <summary>The session's own <c>Hello</c> and the peer's, so that the session is established.</summary>
    public async Task EstablishAsync()
    {
        await Session.WriteAsync(MessageSamples.Hello(), CancellationToken.None);
        Assert.IsType<Hello>(await ReceiveAsync());
        await SendAsync(MessageSamples.Hello());
        Assert.IsType<Hello>((await Session.ReadAsync(CancellationToken.None)).Message);
        Assert.Equal(Amp.Established, Session.State);
    }

    /// <summary>
    /// Moves the clock a second at a time until <paramref name="ending"/> completes, at most <paramref name="cap"/> in all
    /// and within the real-time deadline. The session arms its idle timer when it next waits for input, which on a loaded
    /// machine may be after a single large advance of the clock: then that advance arms nothing, and a test that awaited
    /// the close after it would wait for ever. Stepping lets a late timer fire on a later step.
    /// </summary>
    /// <typeparam name="T">The task's result.</typeparam>
    /// <param name="ending">What completes once enough time passed.</param>
    /// <param name="cap">How far the clock may move.</param>
    /// <returns>The task's result.</returns>
    public async Task<T> AdvanceUntilAsync<T>(Task<T> ending, TimeSpan cap)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        var moved = TimeSpan.Zero;
        while (!ending.IsCompleted)
        {
            Assert.False(deadline.IsCancellationRequested, $"Nothing completed within {Deadline.TotalSeconds:0} s of real time.");
            Assert.True(moved < cap, $"Nothing completed within {cap.TotalSeconds:0} s on the clock.");
            Time.Advance(TimeSpan.FromSeconds(1));
            moved += TimeSpan.FromSeconds(1);
            var pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var timer = new CancellationTokenSource(TimeSpan.FromMilliseconds(2)); // fires once and is released
            timer.Token.UnsafeRegister(static state => ((TaskCompletionSource)state!).TrySetResult(), pause);
            await Task.WhenAny(ending, pause.Task);
        }

        return await ending;
    }

    /// <summary>Reads the session until it fails; the violation it reported, or <see langword="null"/> when it ended without one.</summary>
    public async Task<Violation?> ReadUntilViolationAsync()
    {
        try
        {
            while (!(await Session.ReadAsync(CancellationToken.None)).IsCompleted)
            {
            }

            return null;
        }
        catch (ProtoStream.Errors.ProtocolViolationException violation)
        {
            return violation.Violation;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        decoder.Dispose();
        await peer.Output.CompleteAsync();
        await Connection.DisposeAsync();
    }
}
