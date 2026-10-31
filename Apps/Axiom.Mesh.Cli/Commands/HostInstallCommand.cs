using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh host install [--daemon &lt;path&gt;] [--no-start] [--json]</c>: registers the Host to start at logon, the
/// same registration the install scripts make (fundamentals plan §6e): a per-user scheduled task on Windows, a systemd
/// user unit on Linux; then starts it unless <c>--no-start</c>. The Host is not asked.
/// </summary>
/// <param name="Daemon">The daemon's executable; <see langword="null"/> for <c>axiom-meshd</c> next to this tool.</param>
/// <param name="NoStart">Whether to register only, not start the Host now.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record HostInstallCommand(string? Daemon, bool NoStart, bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        CliMachine machine = context.Environment.Machine;
        string daemon = Daemon is null ? machine.DaemonPath : AutostartTargets.Absolute(Daemon, context.Environment.CurrentDirectory);
        if (!machine.FileExists(daemon))
        {
            throw CliFailure.Problem(Daemon is null
                ? $"no axiom-meshd next to this tool ({daemon}); name the daemon with --daemon <path>"
                : $"no daemon at {daemon}");
        }

        AutostartTarget target = AutostartTargets.Of(machine, context.Environment.Variables, daemon, context.Environment.CurrentDirectory);
        AutostartPlan plan = machine.Platform switch
        {
            LocalPlatform.Windows => ScheduledTaskPlan.Install(target, start: !NoStart),
            LocalPlatform.Linux => SystemdUnitPlan.Install(target, start: !NoStart),
            _ => throw Unsupported(machine.Platform),
        };
        AutostartOutcome outcome = await AutostartShell.RunAsync(plan, machine, context.Cancellation).ConfigureAwait(false);
        return Json ? Print(context, AutostartView.Json(outcome, install: true)) : Print(context, AutostartView.InstallText(outcome));
    }

    /// <summary>The failure for a platform without an autostart.</summary>
    /// <param name="platform">The platform.</param>
    /// <returns>The failure.</returns>
    public static CliFailure Unsupported(LocalPlatform platform) =>
        CliFailure.Problem($"starting the Host at logon is not available on {platform} yet; start it with 'axiom-meshd run'");
}
