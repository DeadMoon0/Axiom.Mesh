using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh pair join [&lt;code|device&gt;]</c>: enters the other side of a pairing (PAIR-SPEC.md §2, §3). Given
/// nothing, lists the devices discovery heard that are not paired yet, to pick one from. Given a device id or a prefix of
/// one, names that device as <c>ListDiscovered</c> shows it (PAIR-SPEC.md §2.4; mode <c>JoinUser</c>). Given a code, as
/// before: a user's code pairs the users (<c>JoinUser</c>), a device's code enrols that device into this user
/// (<c>AddDevice</c>, which needs the user key). Waits until the other device answered, and prints the string to compare.
/// </summary>
/// <param name="Target">What the user typed: a code, a device id or a prefix of one, or <see langword="null"/> to list the discovered devices.</param>
internal sealed record PairJoinCommand(string? Target) : Command
{
    /// <summary>Whether the target is a code the other side showed, rather than a discovered device.</summary>
    public bool IsCode => Target is not null && Target.StartsWith("amp1:", StringComparison.Ordinal);

    /// <summary>The mode the target is entered in: a device's code adds the device, anything else joins its user.</summary>
    public PairingMode Mode => Target is not null && Target.StartsWith("amp1:device:", StringComparison.Ordinal) ? PairingMode.AddDevice : PairingMode.JoinUser;

    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        if (Target is null)
            return await ListAsync(context).ConfigureAwait(false);

        await using PairingWatch watch = await PairingWatch.StartAsync(context).ConfigureAwait(false);
        Pairing started = IsCode
            ? await context.SendAsync(new StartPairing { Mode = Mode, Code = Target }).ConfigureAwait(false)
            : await context.SendAsync(await ByDeviceAsync(context, Target).ConfigureAwait(false)).ConfigureAwait(false);
        context.Error.WriteLine(PairingView.Next(started));
        Pairing connected = await watch.UntilAsync(started.PairingId, p => p.State != PairingState.Waiting).ConfigureAwait(false);
        return PairingResult.Report(context, connected);
    }

    // The devices discovery heard that are in no Roster this Host holds: the ones a pairing could be with.
    private static async Task<IReadOnlyList<Peer>> JoinableAsync(CommandContext context)
    {
        IReadOnlyList<Peer> discovered = await context.SendAsync(new ListDiscovered()).ConfigureAwait(false);
        return [.. discovered.Where(PairingView.IsJoinable)];
    }

    private static async Task<CliExitCode> ListAsync(CommandContext context)
    {
        IReadOnlyList<Peer> joinable = await JoinableAsync(context).ConfigureAwait(false);
        if (joinable.Count == 0)
            throw CliFailure.Problem(PairingView.NothingDiscovered);
        Print(context, PairingView.Discovered(joinable));
        context.Error.WriteLine(PairingView.JoinHint);
        return CliExitCode.Ok;
    }

    // PAIR-SPEC.md §2.4: the device id discovery announced, with the addresses it was heard at.
    private static async Task<StartPairingByDevice> ByDeviceAsync(CommandContext context, string text)
    {
        IReadOnlyList<Peer> joinable = await JoinableAsync(context).ConfigureAwait(false);
        if (!IdMatch.TryFind(text, joinable, p => p.DeviceId.Value, _ => string.Empty, "discovered device", out Peer? match, out string? problem, out bool ambiguous))
            throw ambiguous ? CliFailure.Usage(problem!) : CliFailure.Problem(problem + "; " + PairingView.JoinHint);
        return new StartPairingByDevice { Mode = PairingMode.JoinUser, Device = match!.DeviceId, Addresses = match.Addresses };
    }
}
