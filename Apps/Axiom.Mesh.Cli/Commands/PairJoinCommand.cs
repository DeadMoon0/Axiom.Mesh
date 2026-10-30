using System;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh pair join &lt;code&gt;</c>: enters the other side's code (PAIR-SPEC.md §3): a user's code pairs the users
/// (<c>JoinUser</c>), a device's code enrols that device into this user (<c>AddDevice</c>, which needs the user key).
/// Waits until the code's device answered, and prints the string to compare.
/// </summary>
/// <param name="Code">The code as the other side shows it.</param>
internal sealed record PairJoinCommand(string Code) : Command
{
    /// <summary>The mode a code is entered in: a device's code adds the device, any other joins its user.</summary>
    public PairingMode Mode => Code.StartsWith("amp1:device:", StringComparison.Ordinal) ? PairingMode.AddDevice : PairingMode.JoinUser;

    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        await using PairingWatch watch = await PairingWatch.StartAsync(context).ConfigureAwait(false);
        Pairing started = await context.SendAsync(new StartPairing { Mode = Mode, Code = Code }).ConfigureAwait(false);
        context.Error.WriteLine(PairingView.Next(started));
        Pairing connected = await watch.UntilAsync(started.PairingId, p => p.State != PairingState.Waiting).ConfigureAwait(false);
        return PairingResult.Report(context, connected);
    }
}
