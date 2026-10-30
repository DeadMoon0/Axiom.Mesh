using System;
using System.IO;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host;

/// <summary>
/// Where the daemon keeps everything (fundamentals plan §2.6): its configuration, its keys, its store and database, and
/// its logs. One directory when <c>--home</c> or <c>AXIOM_MESH_HOME</c> names it; else the platform's places: on Windows
/// <c>%LOCALAPPDATA%\Axiom.Mesh</c>, on Linux the XDG configuration, data and state directories.
/// </summary>
internal sealed record HostPaths
{
    /// <summary>The environment variable that names a home of its own.</summary>
    public const string HomeVariable = "AXIOM_MESH_HOME";

    /// <summary>The home, as printed at start: the one directory, or the configuration's directory.</summary>
    public required string Home { get; init; }

    /// <summary>The configuration file, <c>config.json</c>.</summary>
    public required string ConfigFile { get; init; }

    /// <summary>The key directory.</summary>
    public required string Keys { get; init; }

    /// <summary>The store directory: object files under <c>objects</c>, the database under <c>index</c>.</summary>
    public required string Data { get; init; }

    /// <summary>The log directory.</summary>
    public required string Logs { get; init; }

    /// <summary>Where the daemon keeps everything, from its platform, its environment and its command line.</summary>
    /// <param name="platform">The platform.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="home">The <c>--home</c> argument; <see langword="null"/> when not given.</param>
    /// <returns>The paths, absolute.</returns>
    /// <exception cref="MeshConfigurationException">No home is given and the platform's variables are missing.</exception>
    public static HostPaths Resolve(LocalPlatform platform, Func<string, string?> environment, string? home)
    {
        ArgumentNullException.ThrowIfNull(environment);
        string? single = !string.IsNullOrWhiteSpace(home) ? home : environment(HomeVariable) is { Length: > 0 } variable ? variable : null;
        if (single is not null)
            return InOne(Path.GetFullPath(single));

        if (platform == LocalPlatform.Windows)
        {
            string localAppData = environment("LOCALAPPDATA") is { Length: > 0 } local
                ? local
                : throw Missing("LOCALAPPDATA");
            return InOne(Path.Combine(localAppData, "Axiom.Mesh"));
        }

        string userHome = environment("HOME") is { Length: > 0 } h ? h : throw Missing("HOME");
        string config = Path.Combine(Xdg(environment, "XDG_CONFIG_HOME", Path.Combine(userHome, ".config")), "axiom-mesh");
        string data = Path.Combine(Xdg(environment, "XDG_DATA_HOME", Path.Combine(userHome, ".local", "share")), "axiom-mesh");
        string state = Path.Combine(Xdg(environment, "XDG_STATE_HOME", Path.Combine(userHome, ".local", "state")), "axiom-mesh");
        return new HostPaths
        {
            Home = config,
            ConfigFile = Path.Combine(config, "config.json"),
            Keys = Path.Combine(data, "keys"),
            Data = Path.Combine(data, "data"),
            Logs = Path.Combine(state, "logs"),
        };
    }

    private static HostPaths InOne(string home) => new()
    {
        Home = home,
        ConfigFile = Path.Combine(home, "config.json"),
        Keys = Path.Combine(home, "keys"),
        Data = Path.Combine(home, "data"),
        Logs = Path.Combine(home, "logs"),
    };

    // XDG Base Directory: a variable that is unset, empty or not absolute is ignored.
    private static string Xdg(Func<string, string?> environment, string name, string fallback) =>
        environment(name) is { Length: > 0 } value && Path.IsPathRooted(value) ? value : fallback;

    private static MeshConfigurationException Missing(string variable) => new(
        $"The daemon finds its home through {variable}, which is not set.",
        $"Start it with --home <directory>, or set {HomeVariable} to the directory it should keep its configuration, keys and store in.");
}
