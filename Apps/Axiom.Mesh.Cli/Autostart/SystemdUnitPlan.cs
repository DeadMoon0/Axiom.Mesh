using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The Linux autostart (fundamentals plan §6e): a systemd user unit <see cref="UnitName"/> in the user's unit directory,
/// enabled for <c>default.target</c> (started with the user's service manager, at logon), restarted on failure except for
/// the daemon's own refusals (usage, configuration, pipe taken). Pure: the unit text and the <c>systemctl --user</c> calls
/// are planned from the target.
/// </summary>
internal static class SystemdUnitPlan
{
    /// <summary>The unit's name.</summary>
    public const string UnitName = "axiom-meshd.service";

    /// <summary>The program that loads, enables, starts and stops user units.</summary>
    public const string Systemctl = "systemctl";

    /// <summary>Writes and enables the unit and, with <paramref name="start"/>, (re)starts the Host now.</summary>
    /// <param name="target">The daemon, home and unit directory.</param>
    /// <param name="start">Whether to start the Host now.</param>
    /// <returns>The plan.</returns>
    public static AutostartPlan Install(AutostartTarget target, bool start)
    {
        ArgumentNullException.ThrowIfNull(target);
        var steps = new List<AutostartStep>
        {
            new WriteStep(UnitFile(target), Unit(target), Utf16: false),
            User("daemon-reload"),
            User("enable", UnitName),
        };
        if (start)
            steps.Add(User("restart", UnitName));
        return Plan(target, start, steps);
    }

    /// <summary>Stops and disables the unit and deletes its file; a unit that does not exist is no failure.</summary>
    /// <param name="target">The unit directory.</param>
    /// <returns>The plan.</returns>
    public static AutostartPlan Uninstall(AutostartTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Plan(target, start: false,
        [
            new RunStep(Systemctl, ["--user", "disable", "--now", UnitName], MayFail: true, Removes: false),
            new DeleteStep(UnitFile(target), Removes: true),
            User("daemon-reload"),
            new RunStep(Systemctl, ["--user", "reset-failed", UnitName], MayFail: true, Removes: false),
        ]);
    }

    /// <summary>The unit file's path.</summary>
    /// <param name="target">The unit directory.</param>
    /// <returns>The path.</returns>
    public static string UnitFile(AutostartTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.UnitDirectory.TrimEnd('/') + "/" + UnitName;
    }

    /// <summary>The unit's text.</summary>
    /// <param name="target">The daemon and home.</param>
    /// <returns>The text, LF line ends.</returns>
    public static string Unit(AutostartTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        string command = string.Join(' ', AutostartPlans.DaemonArguments(target).Select(Quote).Prepend(Quote(target.Daemon)));
        string[] lines =
        [
            "# Written by axiom-mesh host install; removed by axiom-mesh host uninstall.",
            "[Unit]",
            "Description=Axiom.Mesh Host (axiom-meshd)",
            "Documentation=https://github.com/DeadMoon0/Axiom.Mesh",
            string.Empty,
            "[Service]",
            "Type=simple",
            "ExecStart=" + command,
            "Restart=on-failure",
            "RestartSec=5",
            "# Exit 1 usage, 2 configuration, 3 pipe taken: a restart cannot help; 4 (environment, a busy port) may.",
            "RestartPreventExitStatus=1 2 3",
            string.Empty,
            "[Install]",
            "WantedBy=default.target",
        ];
        return string.Join('\n', lines) + "\n";
    }

    private static RunStep User(params string[] arguments) => new(Systemctl, ["--user", .. arguments], MayFail: false, Removes: false);

    private static AutostartPlan Plan(AutostartTarget target, bool start, IReadOnlyList<AutostartStep> steps) => new()
    {
        System = AutostartSystem.SystemdUser,
        Registration = UnitFile(target),
        Daemon = target.Daemon,
        DaemonArguments = AutostartPlans.DaemonArguments(target),
        Starts = start,
        Steps = steps,
        Notes = AutostartPlans.Notes(target),
    };

    // systemd's command-line quoting: in double quotes with backslash and quote escaped; '%' (specifiers) and '$'
    // (variables) doubled, so a path is taken literally.
    private static string Quote(string argument) => new StringBuilder("\"")
        .Append(new StringBuilder(argument).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("%", "%%").Replace("$", "$$"))
        .Append('"')
        .ToString();
}
