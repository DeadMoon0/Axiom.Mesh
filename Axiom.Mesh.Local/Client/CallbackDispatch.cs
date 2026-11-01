using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Local;

/// <summary>
/// Serves the callbacks a service receives (LOCAL-SPEC.md §3.6, §9.8) off the read loop: the loop records the callback and
/// queues its handler on the thread pool, so the client keeps reading (responses to the handler's own requests included)
/// while it runs. Each handler runs with a deadline of 60 seconds on the client's clock, linked to the connection's end;
/// what it returns is sent once, never more than 120 seconds after the callback arrived, with a write no token cancels
/// (STRUCTURAL-DEBT.md 53: a begun write is never cancelled).
/// </summary>
internal sealed class CallbackDispatch
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(Limits.ServiceCallSeconds);

    private readonly ConcurrentDictionary<uint, long> slots = new();
    private readonly CallbackHandler handler;
    private readonly TimeProvider time;
    private readonly Func<Reply, Task> send;
    private readonly CancellationToken ended;

    /// <summary>The dispatch of one connection.</summary>
    /// <param name="handler">What the service answers with; <see langword="null"/> answers <c>UnknownRequest</c>.</param>
    /// <param name="time">The client's clock: deadlines and the 120-second bound run on it.</param>
    /// <param name="send">Writes one reply; never throws for a connection that ended.</param>
    /// <param name="ended">Cancelled when the connection ends.</param>
    public CallbackDispatch(CallbackHandler? handler, TimeProvider time, Func<Reply, Task> send, CancellationToken ended)
    {
        this.handler = handler ?? NoCallbacks.Instance;
        this.time = time;
        this.send = send;
        this.ended = ended;
    }

    /// <summary>Callbacks received and not yet answered or given up.</summary>
    public int Pending => slots.Count;

    /// <summary>A callback arrived; called on the read loop, which it never holds up.</summary>
    /// <param name="callback">The callback.</param>
    public void Dispatch(Callback callback)
    {
        slots[callback.CallbackId] = time.GetTimestamp();
        ThreadPool.UnsafeQueueUserWorkItem(static work => _ = work.Owner.RunAsync(work.Callback), new Work(this, callback), preferLocal: false);
    }

    private async Task RunAsync(Callback callback)
    {
        CallReply? reply = await AnswerAsync(callback).ConfigureAwait(false);

        // Exactly once: whoever removes the slot sends, and nobody else does.
        if (!slots.TryRemove(callback.CallbackId, out long received) || reply is null)
            return;
        if (!CallbackReplyRule.MaySend(time.GetElapsedTime(received)))
            return;
        await send(reply.ToReply(callback.CallbackId)).ConfigureAwait(false);
    }

    private async Task<CallReply?> AnswerAsync(Callback callback)
    {
        using var deadline = new CancellationTokenSource(Deadline, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, ended);
        try
        {
            return callback.Call switch
            {
                ForwardedCall call => await handler.OnForwardedCallAsync(call, linked.Token).ConfigureAwait(false),
                _ => CallReply.Failure(Status.UnknownRequest, new Error { Message = "This service does not know the callback." }),
            };
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // The Host answered the caller ServiceTimeout at the deadline, or the connection is gone: nothing to send.
            return null;
        }
        catch (Exception ex)
        {
            return CallReply.Failure(Status.IoError, new Error { Message = Cut("The service failed to answer: " + ex.Message, FieldBounds.ErrorMessageLength), Detail = Cut(ex.GetType().FullName ?? ex.GetType().Name, FieldBounds.ErrorDetailLength) });
        }
    }

    // A text cut on a character boundary so that its UTF-8 form fits a string(max).
    private static string Cut(string text, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
            return text;
        int length = Math.Min(text.Length, maxBytes);
        while (length > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, length)) > maxBytes)
            length--;
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
            length--;
        return text[..length];
    }

    private sealed record Work(CallbackDispatch Owner, Callback Callback);

    // A connection that serves no callback answers each UnknownRequest (§1.5), through the handler's default.
    private sealed class NoCallbacks : CallbackHandler
    {
        public static NoCallbacks Instance { get; } = new();
    }
}
