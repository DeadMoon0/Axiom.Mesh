using System;
using System.Threading;

namespace Axiom.Mesh.Core;

/// <summary>
/// What a connection's bytes did, on the node's clock, measured below the protocol so that a <c>Ping</c> or <c>Pong</c>
/// the definition answers by itself counts too: when it last received a byte ("received nothing for 60 seconds", SPEC.md
/// §4.5), how many bytes went each way (LOCAL-SPEC.md §5.8), and, on an AMP/1 connection, whether a frame stays
/// incomplete for 30 seconds after its first byte (SPEC.md §5.1). Set as a connection feature by <see cref="MeshTlsLayer"/>.
/// </summary>
/// <remarks>
/// The frame clock follows the <c>u32</c> length prefix of every frame as the bytes pass, one timer per connection, armed
/// when a read leaves a frame open and disarmed when the frame completes: no allocation per frame. ProtoStream 0.2.0 has
/// no per-frame deadline of its own, so the bound lives here, below the protocol.
/// </remarks>
internal sealed class LinkActivity : IDisposable
{
    private const int HeaderLength = 4;
    private static readonly TimeSpan FrameWindow = TimeSpan.FromSeconds(Limits.FrameCompletionSeconds);

    private readonly TimeProvider time;
    private readonly bool tracksFrames;
    private readonly object gate = new();
    private long lastReceived;
    private long bytesIn;
    private long bytesOut;
    private int headerRead;
    private uint frameLength;
    private long frameRemaining;
    private long frameOpenSince;
    private long armedFor;
    private ITimer? timer;
    private Action? onOverdue;
    private bool overdue;

    /// <summary>Starts counting at the end of the handshake, which received bytes.</summary>
    /// <param name="time">The node's clock.</param>
    /// <param name="tracksFrames">Whether the bytes are AMP/1 frames whose completion is timed (SPEC.md §5.1).</param>
    public LinkActivity(TimeProvider time, bool tracksFrames)
    {
        this.time = time;
        this.tracksFrames = tracksFrames;
        lastReceived = time.GetTimestamp();
    }

    /// <summary>Application bytes received.</summary>
    public long BytesIn => Volatile.Read(ref bytesIn);

    /// <summary>Application bytes sent.</summary>
    public long BytesOut => Volatile.Read(ref bytesOut);

    /// <summary>
    /// Records bytes that arrived now, and follows the frames they belong to. Called on every read that returned data,
    /// by the one reader of the connection.
    /// </summary>
    /// <param name="bytes">The bytes the read returned.</param>
    public void Received(ReadOnlySpan<byte> bytes)
    {
        long now = time.GetTimestamp();
        Volatile.Write(ref lastReceived, now);
        Interlocked.Add(ref bytesIn, bytes.Length);
        if (tracksFrames)
            FollowFrames(bytes, now);
    }

    /// <summary>Records bytes written.</summary>
    /// <param name="count">How many.</param>
    public void Sent(int count) => Interlocked.Add(ref bytesOut, count);

    /// <summary>How long the connection has received nothing.</summary>
    /// <returns>The time since the last byte arrived.</returns>
    public TimeSpan SilentFor() => time.GetElapsedTime(Volatile.Read(ref lastReceived));

    /// <summary>
    /// Calls <paramref name="overdueAction"/> once when a frame is still incomplete 30 seconds after its first byte arrived
    /// (SPEC.md §5.1); at once if that already happened.
    /// </summary>
    /// <param name="overdueAction">What to do: close the session with <c>Bye(Violation)</c>.</param>
    public void OnFrameOverdue(Action overdueAction)
    {
        ArgumentNullException.ThrowIfNull(overdueAction);
        bool already;
        lock (gate)
        {
            onOverdue = overdueAction;
            already = overdue;
        }

        if (already)
            overdueAction();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            timer?.Dispose();
            timer = null;
            onOverdue = null;
        }
    }

    private void FollowFrames(ReadOnlySpan<byte> bytes, long now)
    {
        int at = 0;
        while (at < bytes.Length)
        {
            if (headerRead == 0 && frameRemaining == 0)
                frameOpenSince = now;

            if (headerRead < HeaderLength)
            {
                frameLength = (frameLength << 8) | bytes[at++];
                if (++headerRead == HeaderLength)
                {
                    frameRemaining = frameLength;
                    if (frameRemaining == 0)
                        EndFrame();
                }

                continue;
            }

            int take = (int)Math.Min(frameRemaining, bytes.Length - at);
            frameRemaining -= take;
            at += take;
            if (frameRemaining == 0)
                EndFrame();
        }

        long open = headerRead == 0 ? 0 : frameOpenSince;
        Volatile.Write(ref frameOpenSince, open);
        if (open != 0 && open != armedFor)
            Arm(open);
    }

    private void EndFrame()
    {
        headerRead = 0;
        frameLength = 0;
        frameRemaining = 0;
    }

    private void Arm(long openedAt)
    {
        lock (gate)
        {
            armedFor = openedAt;
            timer ??= time.CreateTimer(static state => ((LinkActivity)state!).Check(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            TimeSpan left = FrameWindow - time.GetElapsedTime(openedAt);
            timer.Change(left < TimeSpan.Zero ? TimeSpan.Zero : left, Timeout.InfiniteTimeSpan);
        }
    }

    // A frame that completed meanwhile, or a newer one that is not yet 30 seconds old, is no violation.
    private void Check()
    {
        long open = Volatile.Read(ref frameOpenSince);
        if (open == 0)
            return;
        TimeSpan elapsed = time.GetElapsedTime(open);
        if (elapsed < FrameWindow)
        {
            Arm(open);
            return;
        }

        Action? action;
        lock (gate)
        {
            if (overdue)
                return;
            overdue = true;
            action = onOverdue;
        }

        action?.Invoke();
    }
}
