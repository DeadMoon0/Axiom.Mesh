using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Service;

/// <summary>
/// Keeps <see cref="ServiceSession.Identity"/> current for one connection (LOCAL-SPEC.md §9.1, §7.2, §6.1): subscribes
/// to topic <c>users</c>, then asks <c>GetIdentity</c> (after the <c>Response</c> to <c>Subscribe</c>, so nothing falls
/// between), and asks again on every <c>UserChanged</c> of the own user and after a lapse. <c>HelloAck.identity</c> is
/// the identity at the moment of connecting only: a service the Host spawned at start connects before
/// <c>CreateIdentity</c> and would otherwise keep an empty one until it reconnects (STRUCTURAL-DEBT.md 81).
/// </summary>
internal sealed class IdentityWatch : IAsyncDisposable
{
    private readonly ServiceSession session;
    private readonly ILogger log;
    private readonly CancellationTokenSource stopping;
    private Subscription<UserChanged>? users;
    private Task loop = Task.CompletedTask;

    private IdentityWatch(ServiceSession session, ILogger log, CancellationToken stop)
    {
        this.session = session;
        this.log = log;
        stopping = CancellationTokenSource.CreateLinkedTokenSource(stop, session.Client.Draining);
    }

    private LocalClient Client => session.Client;

    /// <summary>Subscribes, reads the identity as it stands, and watches from then on.</summary>
    /// <param name="session">The session whose identity is kept.</param>
    /// <param name="log">The runtime's log.</param>
    /// <param name="stop">Stops the watch.</param>
    /// <returns>The watch; dispose it when the session ends.</returns>
    /// <exception cref="RequestFailedException">The Host refused the subscription: the session ends and the runtime reconnects.</exception>
    /// <exception cref="LocalConnectionException">The connection ended.</exception>
    public static async Task<IdentityWatch> StartAsync(ServiceSession session, ILogger log, CancellationToken stop)
    {
        var watch = new IdentityWatch(session, log, stop);
        await watch.SubscribeAndRefreshAsync(stop).ConfigureAwait(false);
        watch.loop = Task.Run(watch.RunAsync, CancellationToken.None);
        return watch;
    }

    /// <summary>Stops watching; the subscription ends with the connection.</summary>
    /// <returns>A task that completes when the loop ended.</returns>
    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        await loop.ConfigureAwait(false);
        stopping.Dispose();
    }

    private async Task RunAsync()
    {
        CancellationToken token = stopping.Token;
        try
        {
            while (users is { } current && !token.IsCancellationRequested)
            {
                await foreach (UserChanged changed in current.WithCancellation(token).ConfigureAwait(false))
                {
                    if (changed.User.Self)
                        await RefreshAsync(token).ConfigureAwait(false);
                }

                if (current.Lapse is not { } lapse)
                    return;

                // The Host dropped events of the own user, maybe: subscribe again and read what stands now.
                ServiceLog.Lapsed(log, "users", lapse.Dropped);
                await SubscribeAndRefreshAsync(token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or LocalConnectionException or RequestFailedException or ObjectDisposedException)
        {
            // The session ends; the next connection starts a watch of its own.
        }
    }

    private async Task SubscribeAndRefreshAsync(CancellationToken cancellationToken)
    {
        LocalResult<Subscription<UserChanged>> subscribed = await Client.SubscribeAsync<UserChanged>(Filter.Any, cancellationToken).ConfigureAwait(false);
        if (!subscribed.IsOk)
            throw new RequestFailedException($"The Host answered Subscribe(users) with {subscribed.Status}: {subscribed.Error?.Message}");
        users = subscribed.Value;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        LocalResult<Identity> identity = await Client.SendAsync(new GetIdentity(), cancellationToken).ConfigureAwait(false);
        if (identity.IsOk)
            session.SetIdentity(identity.Value.Present ? identity.Value : null);
        else
            ServiceLog.IdentityNotRead(log, identity.Status.ToString(), identity.Error?.Message ?? string.Empty);
    }
}
