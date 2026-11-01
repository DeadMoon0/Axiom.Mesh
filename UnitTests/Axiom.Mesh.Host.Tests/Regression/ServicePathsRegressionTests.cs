using System.Collections.Generic;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Host.Tests.Regression;

/// <summary>
/// A service run by hand finds its state directory and the Host's <c>config.json</c> by a copy of the Host's rules
/// (<see cref="ServicePaths"/>), because a service must not reference the Host (S-D1). This keeps the copy equal to the
/// original, and the variable the Host sets for its children equal to the one the runtime reads.
/// </summary>
public sealed class ServicePathsRegressionTests
{
    // Damage: the Host moves its services' state (another directory, another variable) and a service started by hand opens a
    // second, empty index beside the one its spawned runs keep, and re-indexes everything into it.
    [Theory]
    [InlineData(LocalPlatform.Windows, "AXIOM_MESH_HOME", "C:/mesh-home")] // a home of its own on Windows
    [InlineData(LocalPlatform.Linux, "AXIOM_MESH_HOME", "/srv/mesh-home")] // a home of its own on Linux
    [InlineData(LocalPlatform.Windows, "LOCALAPPDATA", "C:/Users/alice/AppData/Local")] // the Windows default
    [InlineData(LocalPlatform.Linux, "HOME", "/home/alice")] // the XDG default under HOME
    [InlineData(LocalPlatform.Linux, "XDG_DATA_HOME", "/home/alice/.data")] // an XDG data directory
    [InlineData(LocalPlatform.Linux, "XDG_CONFIG_HOME", "/home/alice/.cfg")] // an XDG configuration directory
    public void AServiceRunByHandFindsItsStateAndConfigJsonWhereTheHostKeepsThem(LocalPlatform platform, string variable, string value)
    {
        var environment = new Dictionary<string, string?> { ["HOME"] = "/home/alice", [variable] = value };
        string? Read(string name) => environment.TryGetValue(name, out string? found) ? found : null;
        HostPaths host = HostPaths.Resolve(platform, Read, home: null);

        Assert.Equal(host.ServiceState("chat"), ServicePaths.StateDirectory("chat", platform, Read));
        Assert.Equal(host.ConfigFile, ServicePaths.ConfigFile(platform, Read));
    }

    // Damage: the Host hands its children their state directory in a variable the runtime does not read, so every spawned
    // service falls back to the path rule and the Host's own directory goes unused.
    [Fact]
    public void TheHostSetsTheStateVariableTheRuntimeReads()
    {
        Assert.Equal(ServicePaths.StateVariable, ServiceSupervisor.StateVariable);
        Assert.Equal(ServicePaths.HomeVariable, HostPaths.HomeVariable);
    }
}
