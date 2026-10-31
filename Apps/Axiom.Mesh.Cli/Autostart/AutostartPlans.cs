using System;
using System.Collections.Generic;
using System.IO;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>What both autostart plans share: the daemon's arguments, the notes, and the unit directory's rule.</summary>
internal static class AutostartPlans
{
    /// <summary>The daemon's arguments: <c>run</c>, and <c>--home &lt;home&gt;</c> when the target names one.</summary>
    /// <param name="target">The target.</param>
    /// <returns>The arguments.</returns>
    public static IReadOnlyList<string> DaemonArguments(AutostartTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return string.IsNullOrEmpty(target.Home) ? ["run"] : ["run", "--home", target.Home];
    }

    /// <summary>Lines the user should read about the registration.</summary>
    /// <param name="target">The target.</param>
    /// <returns>The lines; empty when there is nothing to say.</returns>
    public static IReadOnlyList<string> Notes(AutostartTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return string.IsNullOrEmpty(target.PipeVariable)
            ? []
            : [$"{PipeLocation.EnvironmentVariable} is set in this shell, and the Host started at logon does not see it; set pipe.name in config.json instead, which the Host and axiom-mesh both read."];
    }

    /// <summary>
    /// The systemd user unit directory by the XDG rule: <c>$XDG_CONFIG_HOME/systemd/user</c> when that is absolute, else
    /// <c>$HOME/.config/systemd/user</c>.
    /// </summary>
    /// <param name="environment">Reads an environment variable.</param>
    /// <returns>The directory; <see langword="null"/> when <c>HOME</c> is unset too.</returns>
    public static string? UnitDirectory(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment("XDG_CONFIG_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg))
            return xdg.TrimEnd('/') + "/systemd/user";
        return environment("HOME") is { Length: > 0 } home ? home.TrimEnd('/') + "/.config/systemd/user" : null;
    }
}
