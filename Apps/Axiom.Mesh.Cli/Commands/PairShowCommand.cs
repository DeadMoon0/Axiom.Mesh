using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh pair show [--device]</c>: starts a pairing that shows a code (PAIR-SPEC.md §3): <c>ShowUser</c> to pair
/// with another user, <c>ShowDevice</c> (with <c>--device</c>) to have this device enrolled by another device of its
/// user. Says first how the other side joins without typing anything (by this device's id, which discovery announces,
/// PAIR-SPEC.md §2.4), prints the code for a LAN without discovery, waits until the other side connected, and prints the
/// string to compare.
/// </summary>
/// <param name="Device">Whether this device is to be enrolled (mode <c>ShowDevice</c>).</param>
internal sealed record PairShowCommand(bool Device) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        await using PairingWatch watch = await PairingWatch.StartAsync(context).ConfigureAwait(false);
        Pairing started = await context.SendAsync(new StartPairing { Mode = Device ? PairingMode.ShowDevice : PairingMode.ShowUser }).ConfigureAwait(false);
        if (!Device)
        {
            // A device being enrolled has no identity yet, so discovery does not announce it: its code is the one way.
            Identity identity = await context.SendAsync(new GetIdentity()).ConfigureAwait(false);
            context.Error.WriteLine(PairingView.Invite(identity.DeviceId));
        }

        Print(context, started.Code);
        if (context.Environment.OutputIsTerminal)
            Print(context, PairingView.Box(started.Code));
        context.Error.WriteLine(PairingView.Next(started));

        Pairing connected = await watch.UntilAsync(started.PairingId, p => p.State != PairingState.Waiting).ConfigureAwait(false);
        return PairingResult.Report(context, connected);
    }
}
