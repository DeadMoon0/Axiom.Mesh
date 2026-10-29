using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Axiom.LWSE.Connections;
using Axiom.Mesh.Local;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Errors;

namespace Axiom.Mesh.Core;

/// <summary>
/// One local connection, served as a thin actor (LOCAL-SPEC.md §2.3–§4.5, §7, §8): one task reads (Hello, then requests,
/// Pongs and Bye), one task writes what the <see cref="Outbox"/> holds, one lane applies ordered requests in arrival
/// order, and a timer keeps liveness. Everything tied to the connection ends with it: requests unanswered,
/// subscriptions gone, operations cancelled, its service name free.
/// </summary>
internal sealed class LocalConnection
{
    private static readonly TimeSpan FrameAccepted = TimeSpan.FromSeconds(Limits.UnacceptedFrameSeconds);

    private readonly LocalPort port;
    private readonly ConnectionContext context;
    private readonly Session<IClientMessage, IHostMessage> session;
    private readonly LinkActivity activity;
    private readonly CancellationTokenSource ending = new();
    private readonly Channel<(RequestRoute Route, RequestFrame Frame)> ordered =
        Channel.CreateBounded<(RequestRoute, RequestFrame)>(new BoundedChannelOptions(Limits.MaxOutstandingRequests) { SingleReader = true });
    private readonly object livenessGate = new();
    private ITimer? liveness;
    private ulong lastNonce;
    private ulong pingNonce;
    private long pongs;
    private long pingSentAt;
    private int draining;
    private int closing;
    private string endReason = "the client closed the connection";

    /// <summary>A connection the local port adopted.</summary>
    /// <param name="port">The port.</param>
    /// <param name="context">LWSE's connection.</param>
    /// <param name="session">The Host side of the local protocol on it.</param>
    /// <param name="origin">Who connected.</param>
    /// <param name="activity">Its bytes, below the protocol: silence and frame completion.</param>
    public LocalConnection(LocalPort port, ConnectionContext context, Session<IClientMessage, IHostMessage> session, ProcessIdentity origin, LinkActivity activity)
    {
        this.port = port;
        this.context = context;
        this.session = session;
        this.activity = activity;
        Origin = origin;
        Outbox = new Outbox(port.Account);
        Operations = new OperationTable(port.Time, Emit, ending.Token);
    }

    /// <summary>LWSE's connection id, for logs and facts.</summary>
    public long Id => context.Id;

    /// <summary>Who connected.</summary>
    public ProcessIdentity Origin { get; }

    /// <summary>The role granted at Ready; <see cref="Role.None"/> before.</summary>
    public Role Role { get; private set; }

    /// <summary>The client's Hello; <see langword="null"/> before Ready.</summary>
    public Hello? Hello { get; private set; }

    /// <summary>The configured service whose name it holds, in the service role.</summary>
    public LocalService? Service { get; private set; }

    /// <summary>What it has to write.</summary>
    public Outbox Outbox { get; }

    /// <summary>Its operations.</summary>
    public OperationTable Operations { get; }

    /// <summary>Whether the Host sent <c>Drain</c> on it (§4.5).</summary>
    public bool IsDraining => Volatile.Read(ref draining) != 0;

    /// <summary>How long nothing was received from the client.</summary>
    public TimeSpan SilentFor => activity.SilentFor();

    /// <summary>The <c>Pong</c>s that answered the Host's <c>Ping</c>s so far.</summary>
    public long Pongs => Interlocked.Read(ref pongs);

    /// <summary>Whether a <c>Ping</c> waits for its <c>Pong</c> (§4.4).</summary>
    public bool PingOutstanding
    {
        get
        {
            lock (livenessGate)
                return pingNonce != 0;
        }
    }

    /// <summary>Cancelled when the connection ends.</summary>
    public CancellationToken Ending => ending.Token;

    /// <summary>Serves the connection until it ends.</summary>
    /// <returns>A task that completes when it ended.</returns>
    public async ValueTask ServeAsync()
    {
        if (port.Book.Arrived(this) is { } oldest)
            oldest.CloseWithoutFrame("a 17th connection arrived while 16 waited for Hello (LOCAL-SPEC.md §2.3)");

        Task writing = Task.CompletedTask;
        Task applying = Task.CompletedTask;
        try
        {
            if (await ReadHelloAsync().ConfigureAwait(false) is not { } hello)
                return;
            if (!await AdmitAsync(hello).ConfigureAwait(false))
                return;

            writing = Task.Run(WriteLoopAsync, CancellationToken.None);
            applying = Task.Run(ApplyOrderedAsync, CancellationToken.None);
            await ReadLoopAsync().ConfigureAwait(false);
        }
        finally
        {
            await EndAsync(writing, applying).ConfigureAwait(false);
        }
    }

    /// <summary>Sends <c>Drain</c> (§4.5): new requests are answered <c>Draining</c>, running operations end Cancelled.</summary>
    /// <param name="grace">How long the Host waits before <c>Bye(Shutdown)</c>.</param>
    public void BeginDrain(TimeSpan grace)
    {
        if (Interlocked.Exchange(ref draining, 1) != 0)
            return;
        uint milliseconds = (uint)Math.Clamp(grace.TotalMilliseconds, 1, Limits.DrainGraceMaxMilliseconds);
        Outbox.Enqueue(OutFrame.Session(new Drain { GraceMilliseconds = milliseconds }));
        Operations.CancelAll();
    }

    /// <summary>The grace ended: <c>Bye(Shutdown)</c> and close (§4.5).</summary>
    public void Shutdown() => CloseWith(Bye.Of(ByeReason.Shutdown, string.Empty), "the Host stopped after Drain (LOCAL-SPEC.md §4.5)");

    /// <summary>Closes with <c>Bye(Unresponsive)</c> (§4.4, §7.4).</summary>
    /// <param name="why">Why, for the Bye's detail and the log.</param>
    public void CloseUnresponsive(string why) => CloseWith(Bye.Of(ByeReason.Unresponsive, why), why);

    /// <summary>Closes a connection without a frame: it waited for <c>Hello</c> too long, or a 17th arrived (§2.3).</summary>
    /// <param name="why">Why, for the log.</param>
    public void CloseWithoutFrame(string why)
    {
        if (Interlocked.Exchange(ref closing, 1) != 0)
            return;
        endReason = why;
        context.Abort();
        ending.Cancel();
    }

    /// <summary>Hands an event to every matching subscription of a topic on this connection.</summary>
    /// <param name="topic">The event's topic.</param>
    /// <param name="match">Whether a subscription's filter takes it.</param>
    /// <param name="queued">The event and its frame's size.</param>
    public void Offer(Topic topic, Func<Filter, bool> match, QueuedEvent queued)
    {
        foreach (LocalSubscription subscription in Outbox.Of(topic))
        {
            if (match(subscription.Filter) && Outbox.Offer(subscription, queued) == OfferResult.Overflow)
            {
                CloseUnresponsive("more than 64 MiB waited for the client to read them (LOCAL-SPEC.md §7.4)");
                return;
            }
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"local #{Id} {Role} \"{Hello?.Name}\" {Origin}";

    private async Task<Hello?> ReadHelloAsync()
    {
        try
        {
            ProtocolReadResult<IClientMessage> first = await session.ReadAsync(ending.Token).ConfigureAwait(false);
            switch (first.Message)
            {
                case Hello hello:
                    return hello;
                case Bye:
                    endReason = "the client said Bye before Hello";
                    return null;
                default:
                    endReason = "the client closed the connection before Hello";
                    return null;
            }
        }
        catch (ProtocolViolationException ex)
        {
            endReason = ex.Violation.Code == ViolationCode.Timeout
                ? "no Hello within 10 seconds; closed without a frame (LOCAL-SPEC.md §2.3)"
                : "the first frame was not Hello: " + ex.Violation.Detail;
            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or ProtoStream.Errors.ProtoStreamException)
        {
            return null;
        }
    }

    private async Task<bool> AdmitAsync(Hello hello)
    {
        LocalHelloVerdict verdict = port.IsDraining
            ? LocalHelloVerdict.Refuse(Bye.Of(ByeReason.Shutdown, "the Host is stopping"))
            : port.Book.Admit(this, hello, port.Options.Services);
        if (verdict.Refusal is { } refusal)
        {
            endReason = $"Hello refused with {refusal}";
            port.Facts.Record(NodeFactKind.LocalRefused, Id, null, default, $"{refusal} for {hello.Role} \"{hello.Name}\", {Origin}");
            await WriteQuietlyAsync(refusal).ConfigureAwait(false);
            return false;
        }

        Role = hello.Role;
        Hello = hello;
        Service = verdict.Service;

        // Armed before HelloAck leaves, so that the client's first moment of Ready is already watched.
        activity.OnFrameOverdue(() => CloseWith(Bye.Of(ByeReason.Violation, "a frame was not completed within 30 seconds of its first byte"), "a frame was not completed within 30 seconds (LOCAL-SPEC.md §3.1)"));
        ArmLiveness(TimeSpan.FromSeconds(Limits.LocalPingAfterSeconds));
        if (!await WriteQuietlyAsync(port.AckFor(this)).ConfigureAwait(false))
            return false;
        port.Facts.Record(NodeFactKind.LocalAdmitted, Id, null, default, $"{Role} \"{hello.Name}\" {hello.ClientVersion}, {Origin}");
        return true;
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                ProtocolReadResult<IClientMessage> read = await session.ReadAsync(ending.Token).ConfigureAwait(false);
                switch (read.Message)
                {
                    case null:
                        return;
                    case RequestFrame frame:
                        Accept(frame);
                        break;
                    case Pong pong:
                        Ponged(pong.Nonce);
                        break;
                    case Reply reply:
                        // No callback is ever issued yet (§9.8 is M9), so every Reply names an id the Host never issued (§3.6).
                        CloseWith(Bye.Of(ByeReason.Violation, $"a Reply for callback {reply.CallbackId}, which this Host never issued"), "a Reply for a callback never issued (LOCAL-SPEC.md §3.6)");
                        return;
                    case Bye bye:
                        endReason = $"the client said {bye}";
                        return;
                }
            }
        }
        catch (ProtocolViolationException ex)
        {
            endReason = ex.Violation.Code == ViolationCode.Timeout
                ? "the client stayed silent past the 120-second backstop: Bye(Unresponsive)"
                : "a violation, closed with Bye(Violation): " + ex.Violation.Detail;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or ProtoStream.Errors.ProtoStreamException)
        {
            // The connection ends; endReason says why when this side ended it.
        }
    }

    private void Accept(RequestFrame frame)
    {
        if (!port.Routes.TryGet(frame.Request.MessageId, out RequestRoute route))
        {
            // The codec reads only ids the protocol knows into a RequestFrame; a table without the row is still answered.
            if (!Outbox.Enqueue(LocalAnswer.Failure(frame.Correlation, Status.UnknownRequest, "This Host does not know the request.", string.Empty).Frame()))
                CloseUnresponsive("more than 64 MiB waited for the client to read them (LOCAL-SPEC.md §7.4)");
            return;
        }

        bool permitted = !route.UserKey || DispatchRule.Permits(Origin.Executable, port.Options.ConfineUserKeyRequestsTo, port.PathComparison);
        (Status status, string message) = DispatchRule.Check(route, Role, port.Node.Trust.Self is not null, IsDraining, permitted);
        if (status != Status.Ok)
        {
            Answer(route, LocalAnswer.Failure(frame.Correlation, status, message, string.Empty));
            return;
        }

        if (route.Ordered)
            ordered.Writer.TryWrite((route, frame));
        else
            _ = RunAsync(route, frame);
    }

    private async Task ApplyOrderedAsync()
    {
        try
        {
            await foreach ((RequestRoute route, RequestFrame frame) in ordered.Reader.ReadAllAsync(ending.Token).ConfigureAwait(false))
                await RunAsync(route, frame).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The connection ended; what was not applied is never answered (§2.3).
        }
    }

    private async Task RunAsync(RequestRoute route, RequestFrame frame)
    {
        LocalAnswer answer;
        try
        {
            answer = await route.Handler!(new RequestContext(this, port, port.Node), frame.Correlation, frame.Request, ending.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ending.IsCancellationRequested)
        {
            return;
        }
        catch (MeshNodeException ex) when (ex.Fault == MeshFault.Caller)
        {
            answer = LocalAnswer.Failure(frame.Correlation, Status.Invalid, ex.FriendlyMessage, string.Join(" ", ex.RecoverySteps));
        }
        catch (ObjectDisposedException)
        {
            answer = LocalAnswer.Failure(frame.Correlation, Status.Draining, "The Host is stopping; finish what you can and close the connection.", string.Empty);
        }
        catch (Exception ex)
        {
            LocalLog.HandlerFailed(port.Log, route.Descriptor.Name, Id, ex);
            answer = LocalAnswer.Failure(frame.Correlation, Status.IoError, "The Host could not complete the request: " + ex.Message, ex.GetType().Name);
        }

        Answer(route, answer);
    }

    private void Answer(RequestRoute route, LocalAnswer answer)
    {
        if (route.UserKey)
            LocalLog.UserKeyRequest(port.Log, route.Descriptor.Name, route.Descriptor.Id, Origin.Pid, Origin.Executable, Origin.User, Id, answer.Status.ToString());
        if (!Outbox.Enqueue(answer.Frame()))
            CloseUnresponsive("more than 64 MiB waited for the client to read them (LOCAL-SPEC.md §7.4)");
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            while (true)
            {
                OutWork work = await Outbox.TakeAsync(ending.Token).ConfigureAwait(false);
                (IHostMessage? message, long size) = await MessageOfAsync(work).ConfigureAwait(false);
                if (message is null)
                    continue;

                using (var deadline = new CancellationTokenSource(FrameAccepted, port.Time))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ending.Token, deadline.Token))
                {
                    try
                    {
                        await session.WriteAsync(message, linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ending.IsCancellationRequested)
                    {
                        CloseUnresponsive("a frame was not accepted within 30 seconds (LOCAL-SPEC.md §7.4)");
                        return;
                    }
                }

                if (work.Subscription is { } subscription && message is EventFrame { Event: EntryStored stored })
                    subscription.LastSeqHanded = stored.Entry.StoreSeq;
                Outbox.Written(work, size);
                work.Control?.Written?.Invoke();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException or IOException or ObjectDisposedException or ProtoStream.Errors.ProtoStreamException)
        {
            // The connection ends.
        }
    }

    private async ValueTask<(IHostMessage? Message, long Size)> MessageOfAsync(OutWork work)
    {
        if (work.Control is { } control)
            return (control.Message, control.Size);
        LocalSubscription subscription = work.Subscription!;
        if (work.ReplaySeq == 0)
            return (new EventFrame(subscription.Id, work.Event.Event), work.Event.Size);

        // A replay reads the next Entry only now, as the queue drains (§7.4); one evicted meanwhile is skipped.
        if (await port.Hub.ReplayAsync(work.ReplaySeq, ending.Token).ConfigureAwait(false) is not { } replayed)
            return (null, 0);
        if (!Outbox.HoldReplayed(replayed.Size))
            CloseUnresponsive("more than 64 MiB waited for the client to read them (LOCAL-SPEC.md §7.4)");
        return (new EventFrame(subscription.Id, replayed.Event), replayed.Size);
    }

    private void Emit(Operation operation)
    {
        var changed = new OperationChanged { Operation = operation };
        Offer(Topic.Operations, f => f.OperationId == 0 || f.OperationId == operation.OperationId, new QueuedEvent(changed, FrameSizes.OfEvent(changed), 0));
    }

    private void ArmLiveness(TimeSpan due)
    {
        lock (livenessGate)
        {
            if (ending.IsCancellationRequested)
                return;
            liveness ??= port.Time.CreateTimer(static state => ((LocalConnection)state!).Tick(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            liveness.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
        }
    }

    // The next look is armed under the same lock that changed the state, so that a clock that moves right after a Ping
    // or a Pong finds the timer already set for it.
    private void Tick()
    {
        lock (livenessGate)
        {
            TimeSpan? pingAge = pingNonce == 0 ? null : port.Time.GetElapsedTime(pingSentAt);
            LivenessStep step = LivenessRule.Decide(activity.SilentFor(), pingAge);
            switch (step.Action)
            {
                case LivenessAction.Ping:
                    pingNonce = ++lastNonce;
                    pingSentAt = port.Time.GetTimestamp();
                    ArmLiveness(TimeSpan.FromSeconds(Limits.LocalPongSeconds));
                    Outbox.Enqueue(OutFrame.Session(new Ping { Nonce = pingNonce }));
                    return;
                case LivenessAction.Wait:
                    ArmLiveness(step.Wait);
                    return;
            }
        }

        CloseUnresponsive("no Pong within 30 seconds of the Ping (LOCAL-SPEC.md §4.4)");
    }

    private void Ponged(ulong nonce)
    {
        lock (livenessGate)
        {
            // A Pong with an unknown nonce is ignored (§4.4).
            if (pingNonce == 0 || nonce != pingNonce)
                return;
            pingNonce = 0;
            Interlocked.Increment(ref pongs);
            ArmLiveness(TimeSpan.FromSeconds(Limits.LocalPingAfterSeconds));
        }
    }

    private void CloseWith(Bye bye, string why)
    {
        if (Interlocked.Exchange(ref closing, 1) != 0)
            return;
        endReason = why;
        Outbox.Close();
        ValueTask write;
        try
        {
            write = session.WriteAsync(bye, ending.Token);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or ProtoStream.Errors.ProtoStreamException)
        {
            write = ValueTask.CompletedTask;
        }

        // A Bye the pipe takes at once is sent; one it cannot take is not waited for: the client stopped reading.
        _ = ObserveAsync(write);
        ending.Cancel();
    }

    private async Task<bool> WriteQuietlyAsync(IHostMessage message)
    {
        try
        {
            await session.WriteAsync(message, ending.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException or ProtoStream.Errors.ProtoStreamException)
        {
            return false;
        }
    }

    private static async Task ObserveAsync(ValueTask write)
    {
        try
        {
            await write.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException or ProtoStream.Errors.ProtoStreamException)
        {
            // The connection is gone.
        }
    }

    private async Task EndAsync(Task writing, Task applying)
    {
        ending.Cancel();
        lock (livenessGate)
        {
            liveness?.Dispose();
            liveness = null;
        }

        activity.Dispose();
        Operations.Dispose();
        Outbox.Close();
        ordered.Writer.TryComplete();
        port.Book.Left(this);
        await Task.WhenAll(writing, applying).ConfigureAwait(false);
        port.Facts.Record(NodeFactKind.LocalEnded, Id, null, default, $"{endReason}; {Origin}");
    }
}
