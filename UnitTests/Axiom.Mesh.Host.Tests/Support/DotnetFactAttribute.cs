using System;
using System.IO;
using System.Linq;

namespace Axiom.Mesh.Host.Tests.Support;

/// <summary>
/// A fact that starts a .NET program as a child process under the <c>dotnet</c> host, and is skipped, saying so, where no
/// <c>dotnet</c> can be found: <c>DOTNET_HOST_PATH</c> (set by <c>dotnet test</c>), else the first <c>dotnet</c> on the PATH.
/// </summary>
public sealed class DotnetFactAttribute : FactAttribute
{
    /// <summary>Runs where <see cref="Dotnet"/> finds the host.</summary>
    public DotnetFactAttribute()
    {
        if (Dotnet is null)
            Skip = "No dotnet host was found (DOTNET_HOST_PATH is not set and no dotnet is on the PATH); the child cannot be started.";
    }

    /// <summary>The <c>dotnet</c> host's absolute path, or <see langword="null"/>.</summary>
    public static string? Dotnet
    {
        get
        {
            if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host))
                return host;
            string name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
            return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, name))
                .FirstOrDefault(File.Exists);
        }
    }
}
