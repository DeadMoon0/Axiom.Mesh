using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.ProtoStream;

namespace Axiom.Mesh.Core;

/// <summary>
/// A session read that is never cancelled, only abandoned (STRUCTURAL-DEBT.md 53). ProtoStream 0.2.0 flushes a session's
/// automatic answers (a <c>Pong</c>) under the token of the read that received the <c>Ping</c>; a read cancelled just as
/// that flush ends leaves the pipe writer's cancellation set, and the next write (the <c>Bye</c> that follows a violation
/// or a drain) is encrypted and then refused, a gap in the TLS record sequence the peer reads as "decryption failed". So
/// the read runs without a token and is raced against what would have cancelled it; an abandoned read ends when the
/// connection closes, and nothing it read is acted on.
/// </summary>
internal static class AbandonableRead
{
    /// <summary>Reads the next message, unless <paramref name="until"/> is cancelled first.</summary>
    /// <typeparam name="TIn">What the session reads.</typeparam>
    /// <typeparam name="TOut">What it writes.</typeparam>
    /// <param name="session">The session.</param>
    /// <param name="until">Abandons the read when cancelled; the read itself is never cancelled.</param>
    /// <returns>The read, or <see langword="null"/> when it was abandoned.</returns>
    public static async Task<ProtocolReadResult<TIn>?> ReadAsync<TIn, TOut>(Session<TIn, TOut> session, CancellationToken until)
        where TIn : class
        where TOut : class
    {
        ArgumentNullException.ThrowIfNull(session);
        Task<ProtocolReadResult<TIn>> read = session.ReadAsync(CancellationToken.None).AsTask();
        if (read.IsCompleted)
            return await read.ConfigureAwait(false);
        var abandoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (until.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), abandoned))
        {
            if (await Task.WhenAny(read, abandoned.Task).ConfigureAwait(false) == read)
                return await read.ConfigureAwait(false);
        }

        Abandon(read);
        return null;
    }

    /// <summary>Lets a read go: whatever it ends with is observed and ignored.</summary>
    /// <param name="read">The read.</param>
    public static void Abandon(Task read)
    {
        ArgumentNullException.ThrowIfNull(read);
        _ = read.ContinueWith(static ended => _ = ended.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
