using System;
using System.IO;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// Where the Host keeps its <c>config.json</c>, by the Host's own rule (fundamentals plan §2.6): the directory
/// <c>AXIOM_MESH_HOME</c> names; else on Windows <c>%LOCALAPPDATA%\Axiom.Mesh</c>; else the XDG configuration directory
/// (<c>$XDG_CONFIG_HOME/axiom-mesh</c>, or <c>~/.config/axiom-mesh</c>). The command line reads the <c>cli</c> service's
/// token from it. Kept equal to the Host's rule by a regression test in the Host's suite.
/// </summary>
public static class CliPaths
{
    /// <summary>The environment variable that names a home of its own, as for the Host.</summary>
    public const string HomeVariable = "AXIOM_MESH_HOME";

    /// <summary>The Host's configuration file, from the platform and the environment.</summary>
    /// <param name="platform">The platform.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <returns>The absolute path; <see langword="null"/> when the variables the platform's rule needs are missing.</returns>
    public static string? ConfigFile(LocalPlatform platform, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment(HomeVariable) is { Length: > 0 } home)
            return Path.Combine(Path.GetFullPath(home), "config.json");
        if (platform == LocalPlatform.Windows)
            return environment("LOCALAPPDATA") is { Length: > 0 } local ? Path.Combine(local, "Axiom.Mesh", "config.json") : null;
        if (environment("HOME") is not { Length: > 0 } user)
            return null;

        // XDG Base Directory: a variable that is unset, empty or not absolute is ignored.
        string config = environment("XDG_CONFIG_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg) ? xdg : Path.Combine(user, ".config");
        return Path.Combine(config, "axiom-mesh", "config.json");
    }
}
