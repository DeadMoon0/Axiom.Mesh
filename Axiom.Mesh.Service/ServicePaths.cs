using System;
using System.IO;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Service;

/// <summary>
/// Where a service keeps its state and finds the Host's configuration, by the Host's own rules (LOCAL-SPEC.md §4.2, S-D2):
/// a spawned service is told its state directory in <c>AXIOM_MESH_SERVICE_STATE</c>; a service run by hand (an external
/// one) uses <c>services/&lt;name&gt;</c> under the Host's data root: <c>AXIOM_MESH_HOME</c>, else <c>%LOCALAPPDATA%/Axiom.Mesh</c>
/// on Windows, else the XDG data directory. Kept equal to the Host's rule by a regression test in the Host's suite.
/// </summary>
public static class ServicePaths
{
    /// <summary>The variable a spawned service finds its state directory in (LOCAL-SPEC.md §4.2).</summary>
    public const string StateVariable = "AXIOM_MESH_SERVICE_STATE";

    /// <summary>The variable that names the Host's home, as for the Host and the command line.</summary>
    public const string HomeVariable = "AXIOM_MESH_HOME";

    /// <summary>A service's state directory.</summary>
    /// <param name="serviceName">The service's name.</param>
    /// <param name="platform">The platform.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <returns>The absolute directory; <see langword="null"/> when the variables the platform's rule needs are missing.</returns>
    public static string? StateDirectory(string serviceName, LocalPlatform platform, Func<string, string?> environment)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceName);
        ArgumentNullException.ThrowIfNull(environment);
        if (environment(StateVariable) is { Length: > 0 } state)
            return Path.GetFullPath(state);
        if (environment(HomeVariable) is { Length: > 0 } home)
            return Path.Combine(Path.GetFullPath(home), "services", serviceName);
        if (platform == LocalPlatform.Windows)
            return environment("LOCALAPPDATA") is { Length: > 0 } local ? Path.Combine(local, "Axiom.Mesh", "services", serviceName) : null;
        if (environment("HOME") is not { Length: > 0 } user)
            return null;

        // XDG Base Directory: a variable that is unset, empty or not absolute is ignored.
        string data = environment("XDG_DATA_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg) ? xdg : Path.Combine(user, ".local", "share");
        return Path.Combine(data, "axiom-mesh", "services", serviceName);
    }

    /// <summary>The Host's configuration file, where an external service finds its token (<c>services.&lt;name&gt;.token</c>).</summary>
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
        string config = environment("XDG_CONFIG_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg) ? xdg : Path.Combine(user, ".config");
        return Path.Combine(config, "axiom-mesh", "config.json");
    }
}
