using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Errors;

namespace Axiom.Mesh.Local;

/// <summary>
/// A connection to the Host over the pipe (LOCAL-SPEC.md): one <c>Hello</c>, then requests, subscriptions and, for a
/// service, callbacks, until either side says <c>Bye</c>. The one obvious way for an app, a script or a service to talk
/// to the Host.
/// </summary>
/// <remarks>
/// <para>
/// <b>Requests</b> are values in and values out: <c>await client.SendAsync(new GetPackage { PackageId = id }, ct)</c> is
/// a <see cref="LocalResult{TOk}"/> holding the request's OK body or the status and <see cref="Error"/> the Host
/// refused it with; a refusal is never an exception. The client allocates correlations, keeps at most 64 requests
/// outstanding (the 65th throws <see cref="RequestLimitException"/> before anything is sent) and may send from any
/// number of threads.
/// </para>
/// <para>
/// <b>Events</b> arrive through <see cref="SubscribeAsync{TEvent}"/>; the topic follows from the event type.
/// <b>Callbacks</b> (a service's <c>ForwardedCall</c>s) go to <see cref="LocalClientOptions.Callbacks"/>, each on a worker
/// with its 60-second deadline, while the client keeps reading; each is answered exactly once.
/// <b>Liveness</b> needs nothing: the definition answers the Host's <c>Ping</c>. <b>Exceptions</b> are for the
/// connection only: <see cref="LocalConnectionException"/> when it cannot be opened, is refused or ended.
/// </para>
/// </remarks>
public sealed class LocalClient : IAsyncDisposable
{
    private readonly Connection connection;
    private readonly Session<IHostMessage, IClientMessage> session;
    private readonly object gate = new();
    private readonly Dictionary<uint, PendingRequest> pending = [];
    private readonly Dictionary<uint, ISubscriptionSink> subscriptions = [];
    private readonly CancellationTokenSource draining = new();
    private readonly CancellationTokenSource ended = new();
    private readonly CallbackDispatch callbacks;
    private readonly TaskCompletionSource<LocalConnectionException> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task reading = Task.CompletedTask;
    private uint lastCorrelation;
    private uint lastSubscription;
    private LocalConnectionException? ending;
    private int closeStarted;

    private LocalClient(Connection connection, Session<IHostMessage, IClientMessage> session, HelloAck host, LocalClientOptions options)
    {
        this.connection = connection;
        this.session = session;
        Host = host;
        callbacks = new CallbackDispatch(options.Callbacks, options.TimeProvider, SendReplyAsync, ended.Token);
    }

    /// <summary>What the Host said in its <c>HelloAck</c>: the granted role, its flags, the identity and the store sequence and epoch.</summary>
    public HelloAck Host { get; }

    /// <summary>Cancelled when the Host sent <c>Drain</c> (§4.5): finish what you can and call <see cref="ByeAsync"/>.</summary>
    public CancellationToken Draining => draining.Token;

    /// <summary>Completes, with the reason, when the connection ended, for whatever cause; never faults.</summary>
    public Task<LocalConnectionException> Closed => closed.Task;

    /// <summary>Opens a connection over a stream (the pipe of <see cref="LocalPipe.ConnectAsync"/>) and says <c>Hello</c>.</summary>
    /// <param name="stream">A duplex stream to the Host; the client owns it from now on.</param>
    /// <param name="options">Who this client is.</param>
    /// <param name="cancellationToken">Cancels the opening.</param>
    /// <returns>The client, once the Host answered <c>HelloAck</c>.</returns>
    /// <exception cref="LocalConnectionException">The Host refused the <c>Hello</c> or the connection ended.</exception>
    public static Task<LocalClient> OpenAsync(Stream stream, LocalClientOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        return OpenAsync(Connection.FromStream(stream, ConnectionOptionsFor(options)), options, cancellationToken);
    }

    /// <summary>Opens a connection over a pipe pair, such as an in-process service's in-memory pair, and says <c>Hello</c>.</summary>
    /// <param name="pipe">A duplex pipe to the Host.</param>
    /// <param name="options">Who this client is.</param>
    /// <param name="cancellationToken">Cancels the opening.</param>
    /// <returns>The client, once the Host answered <c>HelloAck</c>.</returns>
    /// <exception cref="LocalConnectionException">The Host refused the <c>Hello</c> or the connection ended.</exception>
    public static Task<LocalClient> OpenAsync(IDuplexPipe pipe, LocalClientOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(options);
        return OpenAsync(Connection.FromPipe(pipe, ConnectionOptionsFor(options)), options, cancellationToken);
    }

    /// <summary>Sends a request and waits for its response.</summary>
    /// <typeparam name="TRequest">The request; inferred.</typeparam>
    /// <typeparam name="TOk">Its OK body; inferred.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Stops waiting; a request already sent is still answered by the Host and its response discarded.</param>
    /// <returns>The OK body, or the status and error the Host refused it with.</returns>
    /// <exception cref="RequestLimitException">64 requests are outstanding already; nothing was sent.</exception>
    /// <exception cref="LocalConnectionException">The connection ended before the response.</exception>
    public async ValueTask<LocalResult<TOk>> SendAsync<TRequest, TOk>(IRequest<TRequest, TOk> request, CancellationToken cancellationToken)
        where TRequest : LocalRequest, IRequest<TRequest, TOk>
    {
        ArgumentNullException.ThrowIfNull(request);
        var waiting = new PendingRequest<TRequest, TOk>();
        uint correlation = Register(waiting);
        try
        {
            await session.WriteAsync(new RequestFrame(correlation, (TRequest)request), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ProtoStreamException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            Unregister(correlation);
            throw Ended(LocalConnectionException.Lost("a request could not be written.", ex));
        }
        catch
        {
            Unregister(correlation);
            throw;
        }

        return await waiting.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Subscribes to the topic of <typeparamref name="TEvent"/> (§7.1).</summary>
    /// <typeparam name="TEvent">The topic's event, such as <see cref="PackageChanged"/> for topic packages.</typeparam>
    /// <param name="filter">What of the topic to receive; <see cref="Filter.Any"/> for everything, live.</param>
    /// <param name="cancellationToken">Stops waiting for the Host's answer.</param>
    /// <returns>The live subscription, or the status the Host refused it with (<c>Invalid</c>, <c>Busy</c>, <c>GapTooLarge</c>, …).</returns>
    /// <exception cref="RequestLimitException">64 requests are outstanding already; nothing was sent.</exception>
    /// <exception cref="LocalConnectionException">The connection ended.</exception>
    public async ValueTask<LocalResult<Subscription<TEvent>>> SubscribeAsync<TEvent>(Filter filter, CancellationToken cancellationToken)
        where TEvent : LocalEvent, IEvent<TEvent>
    {
        ArgumentNullException.ThrowIfNull(filter);
        Subscription<TEvent> subscription;
        lock (gate)
        {
            ThrowIfEnded();
            uint id = NextFree(ref lastSubscription, subscriptions);
            subscription = new Subscription<TEvent>(this, id, filter);

            // Registered before Subscribe is sent: the Host may send the first event right after the response.
            subscriptions.Add(id, subscription);
        }

        LocalResult<NoContent> result;
        try
        {
            result = await SendAsync(new Subscribe { SubscriptionId = subscription.Id, Topic = TEvent.Topic, Filter = filter }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Forget(subscription, null);
            throw;
        }

        if (result.IsOk)
            return LocalResult<Subscription<TEvent>>.Ok(subscription);

        Forget(subscription, null);
        return LocalResult<Subscription<TEvent>>.Failure(result.Status, result.Error);
    }

    /// <summary>Ends a subscription (§7.1); no event of it follows. A subscription that ended already needs nothing.</summary>
    /// <typeparam name="TEvent">The topic's event.</typeparam>
    /// <param name="subscription">The subscription.</param>
    /// <param name="cancellationToken">Stops waiting for the Host's answer.</param>
    /// <returns>Ok, or the status the Host answered.</returns>
    /// <exception cref="LocalConnectionException">The connection ended.</exception>
    public async ValueTask<LocalResult<NoContent>> UnsubscribeAsync<TEvent>(Subscription<TEvent> subscription, CancellationToken cancellationToken)
        where TEvent : LocalEvent, IEvent<TEvent>
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (subscription.IsEnded)
            return LocalResult<NoContent>.Ok(default);

        LocalResult<NoContent> result = await SendAsync(new Unsubscribe { SubscriptionId = subscription.Id }, cancellationToken).ConfigureAwait(false);
        if (result.IsOk || result.Status == Status.NotFound)
            Forget(subscription, null);
        return result;
    }

    /// <summary>Says <c>Bye(Done)</c> and closes the connection (§4.3); outstanding requests fail.</summary>
    /// <param name="cancellationToken">Stops waiting for the <c>Bye</c> to be written.</param>
    /// <returns>A task that completes when the connection is closed.</returns>
    public async ValueTask ByeAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (ending is not null)
                return;
            ending = LocalConnectionException.ClosedHere();
        }

        try
        {
            await session.WriteAsync(Bye.Of(ByeReason.Done, ""), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ProtoStreamException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection is gone already; closing it is all that is left to do.
        }
        finally
        {
            await CloseAsync().ConfigureAwait(false);
            await reading.ConfigureAwait(false);
        }
    }

    /// <summary>Says <c>Bye(Done)</c> unless the connection ended already, and releases it.</summary>
    /// <returns>A task that completes when the connection is closed.</returns>
    public ValueTask DisposeAsync() => ByeAsync(CancellationToken.None);

    /// <inheritdoc />
    public override string ToString() => $"LocalClient({Host.Role}, {(Closed.IsCompleted ? "closed" : "open")})";

    /// <summary>Callbacks received and neither answered nor given up yet (§9.8), for tests.</summary>
    internal int CallbacksPending => callbacks.Pending;

    /// <summary>Ends a subscription this client lapsed itself and tells the Host, in the background (§7.4).</summary>
    internal void ReleaseLapsed(ISubscriptionSink subscription) => _ = UnsubscribeLapsedAsync(subscription);

    private static ConnectionOptions ConnectionOptionsFor(LocalClientOptions options) => new() { TimeProvider = options.TimeProvider };

    private static async Task<LocalClient> OpenAsync(Connection connection, LocalClientOptions options, CancellationToken cancellationToken)
    {
        var hello = new Hello
        {
            Role = options.Role,
            Name = options.Name,
            ClientVersion = options.ClientVersion,
            Capabilities = options.Capabilities,
            Token = options.Token,
        };

        Session<IHostMessage, IClientMessage>? session = null;
        try
        {
            session = await connection.OpenAsync(LocalProtocol.ClientSide, cancellationToken).ConfigureAwait(false);
            await session.WriteAsync(hello, cancellationToken).ConfigureAwait(false);
            ProtocolReadResult<IHostMessage> answer = await session.ReadAsync(cancellationToken).ConfigureAwait(false);
            switch (answer.Message)
            {
                case HelloAck ack:
                    var client = new LocalClient(connection, session, ack, options);
                    client.reading = Task.Run(client.ReadLoopAsync, CancellationToken.None);
                    return client;
                case Bye bye:
                    throw LocalConnectionException.Refused(bye);
                default:
                    throw LocalConnectionException.Lost("the Host closed it before answering Hello.", null);
            }
        }
        catch (Exception ex) when (ex is not LocalConnectionException and not OperationCanceledException and not ArgumentException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw LocalConnectionException.Lost("Hello was not answered.", ex);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task ReadLoopAsync()
    {
        LocalConnectionException why;
        try
        {
            why = await ReadUntilEndAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Whatever ended the loop (a violation, a timeout, a broken pipe) ended the connection; the reason travels on.
            why = LocalConnectionException.Lost(ex.Message, ex);
        }

        Ended(why);
        await CloseAsync().ConfigureAwait(false);
    }

    private async Task<LocalConnectionException> ReadUntilEndAsync()
    {
        while (true)
        {
            ProtocolReadResult<IHostMessage> result = await session.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            switch (result.Message)
            {
                case null:
                    return LocalConnectionException.Lost("the Host closed it without Bye.", null);
                case Response response:
                    if (!Answer(response))
                        return await DefectAsync($"a Response for correlation {response.Correlation} that is not outstanding, or whose body is not its request's layout");
                    break;
                case EventFrame frame:
                    Route(frame);
                    break;
                case Callback callback:
                    // Served off this loop, so that a handler's own requests are answered while it runs (§9.8).
                    callbacks.Dispatch(callback);
                    break;
                case Drain:
                    ThreadPool.UnsafeQueueUserWorkItem(static source => source.Cancel(), draining, preferLocal: false);
                    break;
                case Bye bye:
                    return LocalConnectionException.ClosedByHost(bye);
            }
        }
    }

    // A reply is written whole once begun: no token cancels it (STRUCTURAL-DEBT.md 53). A connection that ended takes none.
    private async Task SendReplyAsync(Reply reply)
    {
        try
        {
            await session.WriteAsync(reply, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ProtoStreamException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection is gone; the Host forgets the callback with it.
        }
    }

    private bool Answer(Response response)
    {
        PendingRequest? waiting;
        lock (gate)
            pending.Remove(response.Correlation, out waiting);
        return waiting is not null && waiting.Complete(response);
    }

    private void Route(EventFrame frame)
    {
        ISubscriptionSink? sink;
        lock (gate)
        {
            // An event on no live subscription follows a lapse or an Unsubscribe in flight; it is dropped (§1.5, §7.1).
            if (!subscriptions.TryGetValue(frame.SubscriptionId, out sink))
                return;
            if (frame.Event is SubscriptionLapsed)
                subscriptions.Remove(frame.SubscriptionId);
        }

        if (frame.Event is SubscriptionLapsed lapsed)
            sink.Lapse(lapsed);
        else
            sink.Deliver(frame.Event);
    }

    // A Host defect is a lost connection (§11.3); the Bye tells the Host's log why, if the pipe still takes it.
    private async Task<LocalConnectionException> DefectAsync(string what)
    {
        try
        {
            await session.WriteAsync(Bye.Of(ByeReason.Unspecified, "Host defect: " + what), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ProtoStreamException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection is gone already.
        }

        return LocalConnectionException.Lost($"the Host sent {what} (LOCAL-SPEC.md §11.3).", null);
    }

    private async Task UnsubscribeLapsedAsync(ISubscriptionSink subscription)
    {
        try
        {
            await SendAsync(new Unsubscribe { SubscriptionId = subscription.Id }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is LocalConnectionException or RequestLimitException)
        {
            // Either the connection ended, which ends the subscription too, or the Host lapses it itself soon.
        }

        lock (gate)
        {
            if (subscriptions.TryGetValue(subscription.Id, out ISubscriptionSink? live) && ReferenceEquals(live, subscription))
                subscriptions.Remove(subscription.Id);
        }
    }

    private uint Register(PendingRequest waiting)
    {
        lock (gate)
        {
            ThrowIfEnded();
            if (pending.Count >= Limits.MaxOutstandingRequests)
                throw new RequestLimitException();
            uint correlation = NextFree(ref lastCorrelation, pending);
            pending.Add(correlation, waiting);
            return correlation;
        }
    }

    private void Unregister(uint correlation)
    {
        lock (gate)
            pending.Remove(correlation);
    }

    private void Forget(ISubscriptionSink subscription, Exception? error)
    {
        lock (gate)
        {
            if (subscriptions.TryGetValue(subscription.Id, out ISubscriptionSink? live) && ReferenceEquals(live, subscription))
                subscriptions.Remove(subscription.Id);
        }

        subscription.End(error);
    }

    private void ThrowIfEnded()
    {
        if (ending is not null)
            throw ending.Again();
    }

    // The next id after the last one handed out that is neither 0 nor in use; with at most 64 in use one is always near.
    private static uint NextFree<T>(ref uint last, Dictionary<uint, T> inUse)
    {
        do
        {
            last = unchecked(last + 1);
        }
        while (last == 0 || inUse.ContainsKey(last));
        return last;
    }

    // Records why the connection ended (the first reason wins) and fails everything that waits on it.
    private LocalConnectionException Ended(LocalConnectionException why)
    {
        PendingRequest[] waiting;
        ISubscriptionSink[] live;
        lock (gate)
        {
            ending ??= why;
            why = ending;
            waiting = [.. pending.Values];
            live = [.. subscriptions.Values];
            pending.Clear();
            subscriptions.Clear();
        }

        foreach (PendingRequest request in waiting)
            request.Fail(why.Again());
        foreach (ISubscriptionSink subscription in live)
            subscription.End(why.Again());
        return why;
    }

    // Closes once, whoever asks first: the read loop when the connection ended, or ByeAsync.
    private async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref closeStarted, 1) != 0)
        {
            await closed.Task.ConfigureAwait(false);
            return;
        }

        LocalConnectionException why = Ended(LocalConnectionException.ClosedHere());
        await connection.DisposeAsync().ConfigureAwait(false);
        closed.TrySetResult(why);
    }
}
