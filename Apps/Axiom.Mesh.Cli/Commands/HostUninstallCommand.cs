using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh host uninstall [--daemon &lt;path&gt;] [--json]</c>: stops the Host the logon registration runs and removes
/// the registration <c>host install</c> or the install scripts made; nothing registered is no failure. The binaries and
/// the Host's home stay.
/// </summary>
/// <param name="Daemon">The daemon's executable, for stopping it; <see langword="null"/> for <c>axiom-meshd</c> next to this tool.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record HostUninstallCommand(string? Daemon, bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        CliMachine machine = context.Environment.Machine;
        string daemon = Daemon is null ? machine.DaemonPath : AutostartTargets.Absolute(Daemon, context.Environment.CurrentDirectory);
        AutostartTarget target = AutostartTargets.Of(machine, context.Environment.Variables, daemon, context.Environment.CurrentDirectory);
        AutostartPlan plan = machine.Platform switch
        {
            LocalPlatform.Windows => ScheduledTaskPlan.Uninstall(target),
            LocalPlatform.Linux => SystemdUnitPlan.Uninstall(target),
            _ => throw HostInstallCommand.Unsupported(machine.Platform),
        };
        AutostartOutcome outcome = await AutostartShell.RunAsync(plan, machine, context.Cancellation).ConfigureAwait(false);
        return Json ? Print(context, AutostartView.Json(outcome, install: false)) : Print(context, AutostartView.UninstallText(outcome));
    }
}
