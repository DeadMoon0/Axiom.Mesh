using System;

namespace Axiom.Mesh.Local;

/// <summary>
/// Where the Host's pipe is (LOCAL-SPEC.md §2.1): the kind and the full name, a Windows pipe path such as
/// <c>\\.\pipe\axiom-mesh</c> or a socket file path. Both the Host and every client resolve it the same way, and a
/// client never guesses any other name.
/// </summary>
public sealed record PipeLocation
{
    /// <summary>The environment variable that names the pipe instead of the default (§2.1, decision 9).</summary>
    public const string EnvironmentVariable = "AXIOM_MESH_PIPE";

    /// <summary>The pipe's default name.</summary>
    public const string DefaultName = "axiom-mesh";

    /// <summary>The prefix of every Windows pipe path.</summary>
    public const string WindowsPipePrefix = @"\\.\pipe\";

    /// <summary>What carries the protocol.</summary>
    public required PipeKind Kind { get; init; }

    /// <summary>The full name: the Windows pipe path, or the socket file path.</summary>
    public required string Path { get; init; }

    /// <summary>The name a Windows named pipe is opened with, the path without <see cref="WindowsPipePrefix"/>; the path itself for a socket.</summary>
    public string PipeName => Kind == PipeKind.NamedPipe && Path.StartsWith(WindowsPipePrefix, StringComparison.OrdinalIgnoreCase)
        ? Path[WindowsPipePrefix.Length..]
        : Path;

    /// <summary>
    /// Resolves the pipe: <c>AXIOM_MESH_PIPE</c> when set and not empty, else the configured name (the Host's
    /// <c>pipe.name</c>), else the platform's default of §2.1. On
    /// Windows a name without the <c>\\.\pipe\</c> prefix gets it.
    /// </summary>
    /// <param name="platform">The platform.</param>
    /// <param name="environment">The variables that decide the name.</param>
    /// <returns>The location.</returns>
    /// <exception cref="MeshConfigurationException">The platform has no pipe, or the variable the default needs is missing.</exception>
    public static PipeLocation Resolve(LocalPlatform platform, PipeEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        string? named = !string.IsNullOrEmpty(environment.Pipe) ? environment.Pipe
            : !string.IsNullOrEmpty(environment.ConfiguredName) ? environment.ConfiguredName
            : null;
        return platform switch
        {
            LocalPlatform.Windows => new PipeLocation { Kind = PipeKind.NamedPipe, Path = WindowsPath(named ?? DefaultName) },
            LocalPlatform.Linux => new PipeLocation { Kind = PipeKind.UnixSocket, Path = named ?? LinuxDefault(environment) },
            LocalPlatform.MacOS => new PipeLocation { Kind = PipeKind.UnixSocket, Path = named ?? MacDefault(environment) },
            _ => throw new MeshConfigurationException(
                $"{platform} has no pipe to the Host (LOCAL-SPEC.md §2.1): the app hosts the Host and connects over an in-memory stream pair.",
                "Open the LocalClient over the in-memory pair the app's Host hands out, instead of a pipe."),
        };
    }

    /// <inheritdoc />
    public override string ToString() => $"{Kind} {Path}";

    private static string WindowsPath(string name) =>
        name.StartsWith(WindowsPipePrefix, StringComparison.OrdinalIgnoreCase) ? name : WindowsPipePrefix + name;

    private static string LinuxDefault(PipeEnvironment environment)
    {
        if (!string.IsNullOrEmpty(environment.XdgRuntimeDir))
            return Socket(environment.XdgRuntimeDir);
        if (environment.UserId is { } uid)
            return $"/run/user/{uid}/{DefaultName}.sock";
        throw new MeshConfigurationException(
            "Neither XDG_RUNTIME_DIR nor the user id is known, so the Host's socket cannot be located (LOCAL-SPEC.md §2.1).",
            "Run in a login session, which sets XDG_RUNTIME_DIR.",
            $"Or set {EnvironmentVariable} to the socket's path for the Host and this program alike.");
    }

    private static string MacDefault(PipeEnvironment environment)
    {
        if (!string.IsNullOrEmpty(environment.TmpDir))
            return Socket(environment.TmpDir);
        throw new MeshConfigurationException(
            "TMPDIR is not set, so the Host's socket cannot be located (LOCAL-SPEC.md §2.1).",
            "Run in a login session, which sets TMPDIR.",
            $"Or set {EnvironmentVariable} to the socket's path for the Host and this program alike.");
    }

    private static string Socket(string directory) => directory.TrimEnd('/') + "/" + DefaultName + ".sock";
}
