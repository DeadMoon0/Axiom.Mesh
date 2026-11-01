using System.IO;
using System.Text;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh host install [--daemon &lt;path&gt;] [--no-start] [--no-sync] [--json]</c>: registers the Host to start at logon, the
/// same registration the install scripts make (fundamentals plan §6e): a per-user scheduled task on Windows, a systemd
/// user unit on Linux; then starts it unless <c>--no-start</c>. When <c>axiom-mesh-sync</c> lies beside the daemon and the
/// Host's <c>config.json</c> lists no sync service (an older Host wrote it), <c>services.sync</c> is added, unless
/// <c>--no-sync</c> (S-D11, plan §9.4 item 4). The Host is not asked.
/// </summary>
/// <param name="Daemon">The daemon's executable; <see langword="null"/> for <c>axiom-meshd</c> next to this tool.</param>
/// <param name="NoStart">Whether to register only, not start the Host now.</param>
/// <param name="NoSync">Whether to leave config.json without a sync service.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record HostInstallCommand(string? Daemon, bool NoStart, bool NoSync, bool Json) : Command
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

        bool syncAdded = !NoSync && await AddSyncAsync(context, machine, daemon).ConfigureAwait(false);
        AutostartTarget target = AutostartTargets.Of(machine, context.Environment.Variables, daemon, context.Environment.CurrentDirectory);
        AutostartPlan plan = machine.Platform switch
        {
            LocalPlatform.Windows => ScheduledTaskPlan.Install(target, start: !NoStart),
            LocalPlatform.Linux => SystemdUnitPlan.Install(target, start: !NoStart),
            _ => throw Unsupported(machine.Platform),
        };
        AutostartOutcome outcome = await AutostartShell.RunAsync(plan, machine, context.Cancellation).ConfigureAwait(false);
        if (Json)
            return Print(context, AutostartView.Json(outcome, install: true));
        if (syncAdded)
            context.Output.WriteLine($"added the sync service to {context.Environment.ConfigFile}; the Host starts it beside itself");
        return Print(context, AutostartView.InstallText(outcome));
    }

    // S-D11: an axiom-mesh-sync beside the daemon, and a config.json an older Host wrote without services.sync: add it.
    private static async Task<bool> AddSyncAsync(CommandContext context, CliMachine machine, string daemon)
    {
        string sync = Path.Combine(Path.GetDirectoryName(daemon) ?? string.Empty, machine.Platform == LocalPlatform.Windows ? "axiom-mesh-sync.exe" : "axiom-mesh-sync");
        if (!machine.FileExists(sync) || SyncServiceEntry.Added(context.Environment.ReadConfig()) is not { } updated)
            return false;
        await machine.WriteFile(context.Environment.ConfigFile, Encoding.UTF8.GetBytes(updated), context.Cancellation).ConfigureAwait(false);
        return true;
    }

    /// <summary>The failure for a platform without an autostart.</summary>
    /// <param name="platform">The platform.</param>
    /// <returns>The failure.</returns>
    public static CliFailure Unsupported(LocalPlatform platform) =>
        CliFailure.Problem($"starting the Host at logon is not available on {platform} yet; start it with 'axiom-meshd run'");
}
