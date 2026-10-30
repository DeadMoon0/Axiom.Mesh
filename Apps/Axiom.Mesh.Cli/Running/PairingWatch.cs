using System;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// Follows the Host's pairing (LOCAL-SPEC.md §6.2, §7.3) until it reaches a state: subscribed before anything else, so
/// no <c>PairingChanged</c> is missed. Bounded by the pairing's own expiry, after which the Host fails it.
/// </summary>
internal sealed class PairingWatch : IAsyncDisposable
{
    private readonly CommandContext context;
    private readonly LocalClient client;
    private Subscription<PairingChanged> events;

    private PairingWatch(CommandContext context, LocalClient client, Subscription<PairingChanged> events)
    {
        this.context = context;
        this.client = client;
        this.events = events;
    }

    /// <summary>Subscribes to the Host's pairing as the user.</summary>
    /// <param name="context">The command's context.</param>
    /// <returns>The watch.</returns>
    public static async Task<PairingWatch> StartAsync(CommandContext context)
    {
        LocalClient client = await context.UserAsync().ConfigureAwait(false);
        return new PairingWatch(context, client, await context.SubscribeAsync<PairingChanged>(client, Filter.Any).ConfigureAwait(false));
    }

    /// <summary>Waits until the pairing satisfies a condition, reading it once first.</summary>
    /// <param name="pairing">Which pairing.</param>
    /// <param name="condition">What must hold; an ended pairing ends the wait too.</param>
    /// <returns>The pairing.</returns>
    public async Task<Pairing> UntilAsync(uint pairing, Func<Pairing, bool> condition)
    {
        Pairing now = context.Ok(await context.TrySendAsync(client, new GetPairing()).ConfigureAwait(false));
        while (now.PairingId == pairing && !condition(now) && !PairingView.IsEnded(now))
        {
            bool changed = false;
            await foreach (PairingChanged change in events.WithCancellation(context.Cancellation).ConfigureAwait(false))
            {
                if (change.Pairing.PairingId != pairing)
                    continue;
                now = change.Pairing;
                changed = true;
                break;
            }

            if (!changed)
            {
                // The subscription lapsed: read the pairing again and follow it anew.
                await events.DisposeAsync().ConfigureAwait(false);
                events = await context.SubscribeAsync<PairingChanged>(client, Filter.Any).ConfigureAwait(false);
                now = context.Ok(await context.TrySendAsync(client, new GetPairing()).ConfigureAwait(false));
            }
        }

        return now;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => events.DisposeAsync();
}
