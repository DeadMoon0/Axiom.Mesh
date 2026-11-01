using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Local;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli;

/// <summary>
/// What a running command reaches: the environment, its two output streams, and the Host, connected on first use as
/// the user (most commands) or as the external service <c>cli</c> with its configured token (the write commands). Every
/// request waits at most <see cref="CliEnvironment.RequestTimeout"/>; a refusal, an unreachable Host and a timeout leave
/// as a <see cref="CliFailure"/>.
/// </summary>
internal sealed class CommandContext : IAsyncDisposable
{
    /// <summary>The name the command line gives itself in its <c>Hello</c> as the user.</summary>
    public const string ClientName = "axiom-mesh";

    /// <summary>The external service the write commands connect as (decision D4).</summary>
    public const string ServiceName = "cli";

    private LocalClient? user;
    private LocalClient? service;

    /// <summary>A context.</summary>
    /// <param name="environment">The environment.</param>
    /// <param name="output">Standard output: results.</param>
    /// <param name="error">Standard error: failures and progress.</param>
    /// <param name="cancellation">Cancelled by Ctrl-C.</param>
    public CommandContext(CliEnvironment environment, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        Environment = environment;
        Output = output;
        Error = error;
        Cancellation = cancellation;
    }

    /// <summary>The environment.</summary>
    public CliEnvironment Environment { get; }

    /// <summary>Standard output.</summary>
    public TextWriter Output { get; }

    /// <summary>Standard error.</summary>
    public TextWriter Error { get; }

    /// <summary>Cancelled by Ctrl-C.</summary>
    public CancellationToken Cancellation { get; }

    /// <summary>This device's name as the Host's <c>HelloAck</c> said it; empty before the first connection.</summary>
    public string DeviceName => user?.Host.Identity.DeviceName ?? service?.Host.Identity.DeviceName ?? string.Empty;

    /// <summary>The connection as the user, opened on first use.</summary>
    /// <returns>The client.</returns>
    public async Task<LocalClient> UserAsync() =>
        user ??= await OpenAsync(new LocalClientOptions { Name = ClientName, ClientVersion = Environment.Version, TimeProvider = Environment.Time }).ConfigureAwait(false);

    /// <summary>
    /// The connection as the external service <c>cli</c>, opened on first use with the token <c>AXIOM_MESH_SERVICE_TOKEN</c>
    /// names, else the one in <c>config.json</c>.
    /// </summary>
    /// <returns>The client.</returns>
    public async Task<LocalClient> ServiceAsync()
    {
        if (service is not null)
            return service;
        ServiceToken token = TokenFrom(Environment.Variables(ServiceToken.EnvironmentVariable), Environment.ReadConfig());
        if (token.IsNone)
            throw CliFailure.Problem($"no token for service 'cli': {Environment.ConfigFile} holds none in services.cli.token; start 'axiom-meshd run' once so it writes one");
        service = await OpenAsync(new LocalClientOptions { Role = Role.Service, Name = ServiceName, Token = token, ClientVersion = Environment.Version, TimeProvider = Environment.Time }).ConfigureAwait(false);
        return service;
    }

    /// <summary>Sends a request as the user; a refusal leaves as a <see cref="CliFailure"/>.</summary>
    /// <typeparam name="TRequest">The request.</typeparam>
    /// <typeparam name="TOk">Its OK body.</typeparam>
    /// <param name="request">The request.</param>
    /// <returns>The OK body.</returns>
    public async Task<TOk> SendAsync<TRequest, TOk>(IRequest<TRequest, TOk> request)
        where TRequest : LocalRequest, IRequest<TRequest, TOk> =>
        Ok(await TrySendAsync(await UserAsync().ConfigureAwait(false), request).ConfigureAwait(false));

    /// <summary>Sends a request as the service <c>cli</c>; a refusal leaves as a <see cref="CliFailure"/>.</summary>
    /// <typeparam name="TRequest">The request.</typeparam>
    /// <typeparam name="TOk">Its OK body.</typeparam>
    /// <param name="request">The request.</param>
    /// <returns>The OK body.</returns>
    public async Task<TOk> SendAsServiceAsync<TRequest, TOk>(IRequest<TRequest, TOk> request)
        where TRequest : LocalRequest, IRequest<TRequest, TOk> =>
        Ok(await TrySendAsync(await ServiceAsync().ConfigureAwait(false), request).ConfigureAwait(false));

    /// <summary>Sends a request on a connection and returns the result, refusals included.</summary>
    /// <typeparam name="TRequest">The request.</typeparam>
    /// <typeparam name="TOk">Its OK body.</typeparam>
    /// <param name="client">The connection.</param>
    /// <param name="request">The request.</param>
    /// <returns>The result.</returns>
    public Task<LocalResult<TOk>> TrySendAsync<TRequest, TOk>(LocalClient client, IRequest<TRequest, TOk> request)
        where TRequest : LocalRequest, IRequest<TRequest, TOk> =>
        WithDeadlineAsync(token => client.SendAsync(request, token).AsTask());

    /// <summary>Subscribes a connection to a topic; a refusal leaves as a <see cref="CliFailure"/>.</summary>
    /// <typeparam name="TEvent">The topic's event.</typeparam>
    /// <param name="client">The connection.</param>
    /// <param name="filter">What of the topic.</param>
    /// <returns>The live subscription.</returns>
    public async Task<Subscription<TEvent>> SubscribeAsync<TEvent>(LocalClient client, Filter filter)
        where TEvent : LocalEvent, IEvent<TEvent> =>
        Ok(await WithDeadlineAsync(token => client.SubscribeAsync<TEvent>(filter, token).AsTask()).ConfigureAwait(false));

    /// <summary>Calls a method of the chat service as the user; a refusal leaves as a <see cref="CliFailure"/>.</summary>
    /// <typeparam name="TRequest">The method's request.</typeparam>
    /// <typeparam name="TReply">Its reply.</typeparam>
    /// <param name="request">The request.</param>
    /// <returns>The reply.</returns>
    public async Task<TReply> ChatAsync<TRequest, TReply>(IChatMethod<TRequest, TReply> request)
        where TRequest : class, IChatMethod<TRequest, TReply>
        where TReply : class, IWireRecord<TReply>
    {
        LocalResult<TReply> result = await TryChatAsync(request).ConfigureAwait(false);
        if (result.Status == Status.ServiceUnavailable)
            throw CliFailure.Problem($"the chat service is not connected to this Host{(result.Error?.Message is { Length: > 0 } why ? $" ({why})" : string.Empty)}; 'axiom-mesh services' shows it");
        return Ok(result);
    }

    /// <summary>Calls a method of the chat service as the user and returns the result, refusals included.</summary>
    /// <typeparam name="TRequest">The method's request.</typeparam>
    /// <typeparam name="TReply">Its reply.</typeparam>
    /// <param name="request">The request.</param>
    /// <returns>The result.</returns>
    public async Task<LocalResult<TReply>> TryChatAsync<TRequest, TReply>(IChatMethod<TRequest, TReply> request)
        where TRequest : class, IChatMethod<TRequest, TReply>
        where TReply : class, IWireRecord<TReply>
    {
        LocalClient client = await UserAsync().ConfigureAwait(false);
        return await WithDeadlineAsync(token => ChatCall.SendAsync(client, request, token)).ConfigureAwait(false);
    }

    /// <summary>Calls a method of the sync service as the user; a refusal leaves as a <see cref="CliFailure"/>.</summary>
    /// <typeparam name="TRequest">The method's request.</typeparam>
    /// <typeparam name="TReply">Its reply.</typeparam>
    /// <param name="request">The request.</param>
    /// <returns>The reply.</returns>
    public async Task<TReply> SyncAsync<TRequest, TReply>(ISyncMethod<TRequest, TReply> request)
        where TRequest : class, ISyncMethod<TRequest, TReply>
        where TReply : class, IWireRecord<TReply>
    {
        LocalClient client = await UserAsync().ConfigureAwait(false);
        LocalResult<TReply> result = await WithDeadlineAsync(token => SyncCall.SendAsync(client, request, token)).ConfigureAwait(false);
        if (result.Status == Status.ServiceUnavailable)
            throw CliFailure.Problem(SyncUnavailable + (result.Error?.Message is { Length: > 0 } why ? $" ({why})" : string.Empty));
        return Ok(result);
    }

    /// <summary>The line for a sync service that is not connected.</summary>
    public const string SyncUnavailable = "the sync service is not connected to this Host; 'axiom-mesh services' shows it";

    /// <summary>The OK body of a result; a refusal leaves as a <see cref="CliFailure"/>.</summary>
    /// <typeparam name="TOk">The OK body.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>The OK body.</returns>
    public TOk Ok<TOk>(LocalResult<TOk> result) =>
        result.IsOk ? result.Value : throw CliFailure.Refused(result.Status, result.Error, DeviceName);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (LocalClient? client in new[] { service, user })
        {
            if (client is null)
                continue;
            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is LocalConnectionException or IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The connection ended already; nothing is left to close.
            }
        }
    }

    /// <summary>The token the service connection presents: the environment variable first, then <c>config.json</c>.</summary>
    /// <param name="variable">The value of <c>AXIOM_MESH_SERVICE_TOKEN</c>.</param>
    /// <param name="config">The text of <c>config.json</c>.</param>
    /// <returns>The token; <see cref="ServiceToken.None"/> when neither names one.</returns>
    public static ServiceToken TokenFrom(string? variable, string? config) =>
        ServiceToken.TryParseHex(variable, out ServiceToken fromVariable) ? fromVariable
        : CliConfig.TryReadCliToken(config, out ServiceToken fromConfig) ? fromConfig
        : ServiceToken.None;

    private async Task<LocalClient> OpenAsync(LocalClientOptions options)
    {
        Stream stream;
        using (var deadline = new CancellationTokenSource(Environment.RequestTimeout, Environment.Time))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, Cancellation))
        {
            try
            {
                stream = await Environment.ConnectHost(linked.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is LocalConnectionException or IOException or UnauthorizedAccessException or TimeoutException or System.Net.Sockets.SocketException
                || (ex is OperationCanceledException && deadline.IsCancellationRequested))
            {
                throw CliFailure.Unreachable(Environment.HostAddress, string.Empty);
            }
        }

        try
        {
            return await WithDeadlineAsync(token => LocalClient.OpenAsync(stream, options, token)).ConfigureAwait(false);
        }
        catch (LocalConnectionException refused)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw CliFailure.Disconnected(refused.FriendlyMessage);
        }
    }

    // A request's deadline on the environment's clock; Ctrl-C cancels as well. A connection that ends meanwhile is a
    // Host that is gone.
    private async Task<T> WithDeadlineAsync<T>(Func<CancellationToken, Task<T>> work)
    {
        using var deadline = new CancellationTokenSource(Environment.RequestTimeout, Environment.Time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, Cancellation);
        try
        {
            return await work(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !Cancellation.IsCancellationRequested)
        {
            throw CliFailure.TimedOut(Environment.RequestTimeout);
        }
        catch (LocalConnectionException ended)
        {
            throw CliFailure.Disconnected(ended.FriendlyMessage);
        }
    }
}
