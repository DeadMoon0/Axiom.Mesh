using System.Collections.Generic;
using Axiom.Mesh.Cli;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host.Tests.Regression;

/// <summary>
/// The command line finds <c>config.json</c> by a copy of the Host's home rule, because it must not reference the Host
/// (plan §1). This keeps the copy equal to the original.
/// </summary>
public sealed class CliHomeRegressionTests
{
    // Damage: the Host moves its configuration (a new variable, another default directory) and the command line keeps
    // reading the old place, so 'write' and 'package publish' find no token for service cli.
    [Theory]
    [InlineData(LocalPlatform.Windows, "AXIOM_MESH_HOME", "C:/mesh-home")] // a home of its own on Windows
    [InlineData(LocalPlatform.Linux, "AXIOM_MESH_HOME", "/srv/mesh-home")] // a home of its own on Linux
    [InlineData(LocalPlatform.Windows, "LOCALAPPDATA", "C:/Users/alice/AppData/Local")] // the Windows default
    [InlineData(LocalPlatform.Linux, "HOME", "/home/alice")] // the XDG default under HOME
    [InlineData(LocalPlatform.Linux, "XDG_CONFIG_HOME", "/home/alice/.cfg")] // an XDG configuration directory
    [InlineData(LocalPlatform.Linux, "XDG_CONFIG_HOME", "relative/cfg")] // a relative XDG directory, which both ignore
    public void TheCommandLineFindsConfigJsonWhereTheHostKeepsIt(LocalPlatform platform, string variable, string value)
    {
        var environment = new Dictionary<string, string?> { ["HOME"] = "/home/alice", [variable] = value };
        string? Read(string name) => environment.TryGetValue(name, out string? found) ? found : null;

        Assert.Equal(HostPaths.Resolve(platform, Read, home: null).ConfigFile, CliPaths.ConfigFile(platform, Read));
    }
}
