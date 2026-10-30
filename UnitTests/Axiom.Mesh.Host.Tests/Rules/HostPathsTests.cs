using System.Collections.Generic;
using System.IO;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>
/// Where the daemon keeps everything (fundamentals plan §2.6): <c>--home</c>, else <c>AXIOM_MESH_HOME</c>, keeps it all
/// in one directory; else Windows uses <c>%LOCALAPPDATA%\Axiom.Mesh</c> and Linux the XDG directories.
/// </summary>
public sealed class HostPathsTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "axiom-mesh-paths"));

    [Fact]
    public void HomeArgumentWinsOverTheVariable_AndKeepsEverythingInOneDirectory()
    {
        string argument = Path.Combine(Root, "argument");
        HostPaths paths = HostPaths.Resolve(LocalPlatform.Linux, Environment(("AXIOM_MESH_HOME", Path.Combine(Root, "variable")), ("HOME", Root)), argument);

        Assert.Equal(argument, paths.Home);
        Assert.Equal(Path.Combine(argument, "config.json"), paths.ConfigFile);
        Assert.Equal(Path.Combine(argument, "keys"), paths.Keys);
        Assert.Equal(Path.Combine(argument, "data"), paths.Data);
        Assert.Equal(Path.Combine(argument, "logs"), paths.Logs);
        Assert.Equal(Path.Combine(Root, "variable"), HostPaths.Resolve(LocalPlatform.Linux, Environment(("AXIOM_MESH_HOME", Path.Combine(Root, "variable"))), null).Home);
    }

    [Fact]
    public void WindowsKeepsItAllUnderLocalAppData()
    {
        HostPaths paths = HostPaths.Resolve(LocalPlatform.Windows, Environment(("LOCALAPPDATA", Root)), null);

        Assert.Equal(Path.Combine(Root, "Axiom.Mesh"), paths.Home);
        Assert.Equal(Path.Combine(Root, "Axiom.Mesh", "keys"), paths.Keys);
    }

    [Fact]
    public void LinuxUsesTheXdgDirectories_TheirDefaultsWhenUnsetOrRelative()
    {
        HostPaths defaults = HostPaths.Resolve(LocalPlatform.Linux, Environment(("HOME", Root), ("XDG_CONFIG_HOME", "relative")), null);
        Assert.Equal(Path.Combine(Root, ".config", "axiom-mesh", "config.json"), defaults.ConfigFile);
        Assert.Equal(Path.Combine(Root, ".local", "share", "axiom-mesh", "keys"), defaults.Keys);
        Assert.Equal(Path.Combine(Root, ".local", "share", "axiom-mesh", "data"), defaults.Data);
        Assert.Equal(Path.Combine(Root, ".local", "state", "axiom-mesh", "logs"), defaults.Logs);

        string state = Path.Combine(Root, "state");
        Assert.Equal(Path.Combine(state, "axiom-mesh", "logs"), HostPaths.Resolve(LocalPlatform.Linux, Environment(("HOME", Root), ("XDG_STATE_HOME", state)), null).Logs);
    }

    [Fact]
    public void WithoutAHomeOrThePlatformsVariablesTheDaemonSaysHowToGiveOne()
    {
        var refused = Assert.Throws<MeshConfigurationException>(() => HostPaths.Resolve(LocalPlatform.Linux, _ => null, null));
        Assert.Contains("--home", Assert.Single(refused.RecoverySteps), System.StringComparison.Ordinal);
    }

    private static System.Func<string, string?> Environment(params (string Name, string Value)[] variables)
    {
        var map = new Dictionary<string, string>();
        foreach ((string name, string value) in variables)
            map[name] = value;
        return name => map.GetValueOrDefault(name);
    }
}
