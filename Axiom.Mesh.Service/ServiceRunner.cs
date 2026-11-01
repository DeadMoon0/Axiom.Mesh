using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.Mesh.Local;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Service;

/// <summary>
/// The loop every service runs (LOCAL-SPEC.md §9.1, §9.3, §2.3): open the state; connect with the token
/// (<c>AXIOM_MESH_SERVICE_TOKEN</c>, else the configuration's for a service run by hand); register every kind; plan the
/// catch-up against the cursor; tell the service; page with <c>ListStored</c> while the gap is large, then subscribe to its
/// Entries from where it stands and to its Packages; hand each event to the service one at a time, advancing the cursor
/// after each Entry; on a lapse subscribe again from the next store sequence; on <c>Drain</c> finish the Entry in hand and
/// say <c>Bye</c>; reconnect by <see cref="ReconnectRule"/> until stopped.
/// </summary>
internal sealed class ServiceRunner
{
    private const ushort PageSize = 64;

    private readonly IMeshService service;
    private readonly ServiceDefinition definition;
    private readonly ServiceEnvironment environment;
    private readonly ILogger log;

    private ServiceRunner(IMeshService service, ServiceEnvironment environment)
    {
        this.service = service;
        definition = service.Definition;
        this.environment = environment;
        log = environment.Loggers.CreateLogger("Axiom.Mesh.Service." + definition.Name);
    }

    /// <summary>Runs a service until <paramref name="stop"/> or until the Host refuses it for good.</summary>
    /// <param name="service">The service.</param>
    /// <param name="environment">Its environment.</param>
    /// <param name="stop">Ctrl-C, SIGTERM, or the test's end.</param>
    /// <returns>What the process exits with.</returns>
    public static Task<ServiceExitCode> RunAsync(IMeshService service, ServiceEnvironment environment, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(environment);
        return new ServiceRunner(service, environment).RunAsync(stop);
    }

    private async Task<ServiceExitCode> RunAsync(CancellationToken stop)
    {
        if (definition.Problems() is { Count: > 0 } problems)
        {
            ServiceLog.Unusable(log, definition.Name, string.Join("; ", problems));
            return ServiceExitCode.Configuration;
        }

        if (TokenOf() is not { } token)
        {
            ServiceLog.NoToken(log, definition.Name, ServiceToken.EnvironmentVariable, environment.ConfigFile);
            return ServiceExitCode.Configuration;
        }

        Database database;
        ServiceState state;
        try
        {
            database = await environment.OpenState(stop).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ServiceLog.StateFailed(log, environment.StateDirectory, ex);
            return ServiceExitCode.State;
        }

        await using (database.ConfigureAwait(false))
        {
            try
            {
                state = await ServiceState.OpenAsync(database, stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ServiceLog.StateFailed(log, environment.StateDirectory, ex);
                return ServiceExitCode.State;
            }

            return await ConnectLoopAsync(token, state, stop).ConfigureAwait(false);
        }
    }

    private async Task<ServiceExitCode> ConnectLoopAsync(ServiceToken token, ServiceState state, CancellationToken stop)
    {
        int attempt = 0;
        while (!stop.IsCancellationRequested)
        {
            ByeReason? refusal = null;
            string detail = string.Empty;
            var calls = new ServiceCalls(service);
            LocalClient? client = null;
            try
            {
                Stream stream = await environment.ConnectHost(stop).ConfigureAwait(false);
                client = await LocalClient.OpenAsync(stream, OptionsOf(token, calls), stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (LocalConnectionException ex)
            {
                refusal = ex.Reason;
                detail = ex.Detail ?? string.Empty;
                ServiceLog.NotConnected(log, environment.HostAddress, ex.FriendlyMessage);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException or MeshException)
            {
                ServiceLog.NotConnected(log, environment.HostAddress, ex.Message);
            }

            if (client is not null)
            {
                attempt = 0;
                ServiceExitCode? exit = await RunSessionAsync(client, calls, state, stop).ConfigureAwait(false);
                if (exit is { } code)
                    return code;

                // A spawned child lives as long as its Host: the Host drains only when it stops, and starts a new child
                // when it runs again. A service run by hand waits for its Host to come back instead.
                if (Spawned && client.Draining.IsCancellationRequested)
                    return ServiceExitCode.Ok;
            }

            if (stop.IsCancellationRequested)
                break;
            ReconnectDecision decision = ReconnectRule.Decide(refusal, detail, ++attempt);
            if (decision.Action == ReconnectAction.Exit)
            {
                ServiceLog.Refused(log, definition.Name, detail);
                return decision.ExitCode;
            }

            ServiceLog.Reconnecting(log, decision.Delay.TotalSeconds);
            try
            {
                await Task.Delay(decision.Delay, environment.Time, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return ServiceExitCode.Ok;
    }

    // One connection: registration, catch-up, the pump. Null when the runtime should reconnect; an exit code when it must stop.
    private async Task<ServiceExitCode?> RunSessionAsync(LocalClient client, ServiceCalls calls, ServiceState state, CancellationToken stop)
    {
        ServiceSession? session = null;
        try
        {
            await using (client.ConfigureAwait(false))
            {
                Registration registration = await RegisterAsync(client, stop).ConfigureAwait(false);
                if (!registration.Registered)
                    return registration.Exit;
                (Hash32 epoch, ulong hostSeq) = (registration.Epoch, registration.HostSeq);

                IndexCursor cursor = await state.ReadCursorAsync(stop).ConfigureAwait(false);
                IndexCatchUp plan = IndexCatchUp.Plan(cursor.Epoch, cursor.LastSeq, epoch);
                session = new ServiceSession(definition, client, epoch, plan, state, log);
                calls.Attach(session);
                ServiceLog.Connected(log, definition.Name, client.Host.HostVersion, plan.FromSeq, plan.DiscardIndex);
                await service.OnConnectedAsync(session, stop).ConfigureAwait(false);
                if (cursor.Epoch != epoch)
                    await state.DiscardAsync(epoch, stop).ConfigureAwait(false);

                await new Pump(this, session, epoch).RunAsync(plan.FromSeq, hostSeq, stop).ConfigureAwait(false);
                if (client.Draining.IsCancellationRequested)
                    ServiceLog.Drained(log);
                return null;
            }
        }
        catch (LocalConnectionException ex)
        {
            ServiceLog.Lost(log, ex.FriendlyMessage);
            return null;
        }
        catch (RequestFailedException ex)
        {
            // The Host refused a request the loop needs (a page, a subscription): reconnect and start over from the cursor.
            ServiceLog.Lost(log, ex.Message);
            return null;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A handler or the state failed: the process exits, and its supervisor restarts it with backoff.
            ServiceLog.HandlerFailed(log, definition.Name, ex);
            return ServiceExitCode.State;
        }
        finally
        {
            if (session is not null)
                await DisconnectedAsync(session).ConfigureAwait(false);
        }
    }

    private async Task<Registration> RegisterAsync(LocalClient client, CancellationToken stop)
    {
        Hash32 epoch = client.Host.StoreEpoch;
        ulong hostSeq = client.Host.StoreSeq;
        foreach (ServiceKind kind in definition.Kinds)
        {
            LocalResult<KindRegistration> registered = await client.SendAsync(new RegisterKind { Kind = kind.Kind, EntryKinds = kind.EntryKinds }, stop).ConfigureAwait(false);
            if (registered.IsOk)
            {
                epoch = registered.Value.StoreEpoch;
                hostSeq = Math.Max(hostSeq, registered.Value.StoreSeq);
                continue;
            }

            ServiceLog.KindRefused(log, (ushort)kind.Kind, registered.Status.ToString(), registered.Error?.Message ?? string.Empty);

            // Another connected service holds the kind for now (the command line, a moment): try again later. Anything else is
            // the configuration's, and retrying unchanged cannot help.
            return registered.Status == Status.KindTaken ? Registration.Retry : Registration.Refused;
        }

        return new Registration(true, epoch, hostSeq, null);
    }

    private async Task DisconnectedAsync(ServiceSession session)
    {
        try
        {
            await service.OnDisconnectedAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ServiceLog.HandlerFailed(log, definition.Name, ex);
        }
    }

    // Spawned by a Host: the token came from the child's environment (LOCAL-SPEC.md §4.2).
    private bool Spawned => environment.Variables(ServiceToken.EnvironmentVariable) is { Length: > 0 };

    private ServiceToken? TokenOf()
    {
        if (environment.Variables(ServiceToken.EnvironmentVariable) is { Length: > 0 } spawned)
            return ServiceToken.TryParseHex(spawned, out ServiceToken token) && !token.IsNone ? token : null;
        return ServiceConfig.TryReadToken(environment.ReadConfig(), definition.Name, out ServiceToken external) && !external.IsNone ? external : null;
    }

    private LocalClientOptions OptionsOf(ServiceToken token, ServiceCalls calls) => new()
    {
        Role = Role.Service,
        Name = definition.Name,
        ClientVersion = definition.Version,
        Token = token,
        Callbacks = calls,
        TimeProvider = environment.Time,
    };

    // The events of one session, one at a time: Entries in store order (after the catch-up pages), Package changes between.
    private sealed class Pump(ServiceRunner runner, ServiceSession session, Hash32 epoch)
    {
        private readonly Channel<object> events = Channel.CreateBounded<object>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        private ulong next;

        private LocalClient Client => session.Client;

        public async Task RunAsync(ulong fromSeq, ulong hostSeq, CancellationToken stop)
        {
            next = Math.Max(fromSeq, 1);
            using var draining = CancellationTokenSource.CreateLinkedTokenSource(stop, Client.Draining);
            CancellationToken pumping = draining.Token;
            try
            {
                Subscription<PackageChanged> packages = await SubscribeAsync<PackageChanged>(Filter.Any with { Kinds = [.. runner.definition.Kinds.Select(k => (ushort)k.Kind)] }, pumping).ConfigureAwait(false);
                _ = ForwardAsync(packages, pumping);
                while (!pumping.IsCancellationRequested)
                {
                    hostSeq = await PageAsync(hostSeq, stop, pumping).ConfigureAwait(false);
                    LocalResult<Subscription<EntryStored>> entries = await Client.SubscribeAsync<EntryStored>(Filter.Any with { Kinds = EntryKinds(), FromSeq = next }, pumping).ConfigureAwait(false);
                    if (entries.Status == Status.GapTooLarge)
                    {
                        // More arrived meanwhile than one replay carries: page again.
                        hostSeq = ulong.MaxValue;
                        continue;
                    }

                    _ = ForwardAsync(Ok(entries, "Subscribe(entries)"), pumping);
                    hostSeq = await DrainEventsAsync(stop, pumping).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (pumping.IsCancellationRequested && !stop.IsCancellationRequested)
            {
                // Drain: the Entry in hand was finished; the session ends with Bye.
            }
            finally
            {
                // Forwarders still running end their writes; nothing reads this session's events any more.
                events.Writer.TryComplete();
            }
        }

        // Pages with ListStored while more than one replay may be missing.
        private async Task<ulong> PageAsync(ulong hostSeq, CancellationToken stop, CancellationToken pumping)
        {
            for (CatchUpStep step = CatchUpStep.Next(next, hostSeq); step.Action == CatchUpAction.Page; step = CatchUpStep.Next(next, hostSeq))
            {
                EntryPage page = Ok(await Client.SendAsync(new ListStored { Kinds = runner.definition.EntryKinds, FromSeq = step.FromSeq, Limit = PageSize }, pumping).ConfigureAwait(false), "ListStored");
                foreach (Entry entry in page.Entries)
                    await HandleAsync(entry, stop).ConfigureAwait(false);
                if (!page.More)
                    return next - 1;
                pumping.ThrowIfCancellationRequested();
            }

            return hostSeq;
        }

        // Hands events to the service until the Entry subscription lapses (then: subscribe again from the next store sequence).
        private async Task<ulong> DrainEventsAsync(CancellationToken stop, CancellationToken pumping)
        {
            while (true)
            {
                object item = await events.Reader.ReadAsync(pumping).ConfigureAwait(false);
                switch (item)
                {
                    case EntryStored stored:
                        await HandleAsync(stored.Entry, stop).ConfigureAwait(false);
                        break;
                    case PackageChanged changed:
                        await runner.service.OnPackageChangedAsync(session, changed, stop).ConfigureAwait(false);
                        break;
                    case Ended { Topic: Topic.Entries, Lapse: { } lapse }:
                        ServiceLog.Lapsed(runner.log, "entries", lapse.Dropped);
                        return next - 1;
                    case Ended { Topic: Topic.Packages, Lapse: { } lapse }:
                        // Changes were dropped: subscribe again; the service rereads what it needs (ListPackages).
                        ServiceLog.Lapsed(runner.log, "packages", lapse.Dropped);
                        _ = ForwardAsync(await SubscribeAsync<PackageChanged>(Filter.Any with { Kinds = [.. runner.definition.Kinds.Select(k => (ushort)k.Kind)] }, pumping).ConfigureAwait(false), pumping);
                        break;
                    case Ended { Error: { } error }:
                        throw error;
                }

                pumping.ThrowIfCancellationRequested();
            }
        }

        private async Task HandleAsync(Entry entry, CancellationToken stop)
        {
            // A replay after a page or a lapse may repeat what was handled; the cursor never goes back.
            if (entry.StoreSeq < next)
                return;
            await runner.service.OnEntryAsync(session, new EntryStored { Entry = entry }, stop).ConfigureAwait(false);

            // The handler is done: its cursor is written whole even when the service is stopping, so that a clean stop
            // repeats nothing (a crash still may, and the handler dedupes by hash).
            await session.State.AdvanceAsync(epoch, entry.StoreSeq, CancellationToken.None).ConfigureAwait(false);
            next = entry.StoreSeq + 1;
        }

        private async Task<Subscription<TEvent>> SubscribeAsync<TEvent>(Filter filter, CancellationToken cancellationToken)
            where TEvent : LocalEvent, IEvent<TEvent> =>
            Ok(await Client.SubscribeAsync<TEvent>(filter, cancellationToken).ConfigureAwait(false), "Subscribe(" + TEvent.Topic + ")");

        private async Task ForwardAsync<TEvent>(Subscription<TEvent> subscription, CancellationToken cancellationToken)
            where TEvent : LocalEvent, IEvent<TEvent>
        {
            Exception? error = null;
            try
            {
                await foreach (TEvent item in subscription.WithCancellation(cancellationToken).ConfigureAwait(false))
                    await events.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
            {
                return;
            }
            catch (LocalConnectionException ex)
            {
                error = ex;
            }

            try
            {
                await events.Writer.WriteAsync(new Ended(TEvent.Topic, subscription.Lapse, error), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
            {
                // The pump stopped.
            }
        }

        private IReadOnlyList<ushort> EntryKinds() => [.. runner.definition.EntryKinds.Select(k => (ushort)k)];

        private static T Ok<T>(LocalResult<T> result, string request) =>
            result.IsOk ? result.Value : throw new RequestFailedException($"The Host answered {request} with {result.Status}: {result.Error?.Message}");
    }

    // A subscription ended: lapsed (resubscribe) or with the connection (the session ends).
    private sealed record Ended(Topic Topic, SubscriptionLapsed? Lapse, Exception? Error);

    // How the registration of every kind went: registered with the epoch and the newest store sequence, or retry, or exit.
    private sealed record Registration(bool Registered, Hash32 Epoch, ulong HostSeq, ServiceExitCode? Exit)
    {
        public static Registration Retry { get; } = new(false, default, 0, null);

        public static Registration Refused { get; } = new(false, default, 0, ServiceExitCode.Configuration);
    }

    // A request the loop needs was refused: the session ends and the runtime reconnects.
    private sealed class RequestFailedException(string message) : Exception(message);
}
