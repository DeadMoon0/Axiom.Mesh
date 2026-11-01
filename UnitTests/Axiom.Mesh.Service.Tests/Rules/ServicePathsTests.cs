using System;
using System.Collections.Generic;
using System.IO;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Service.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §4.2 and S-D2, where a service keeps its state: the directory its Host hands it in
/// <c>AXIOM_MESH_SERVICE_STATE</c>; for a service run by hand <c>services/&lt;name&gt;</c> under the Host's home, or under the
/// platform's data directory; and the Host's configuration file by the Host's rule.
/// </summary>
public sealed class ServicePathsTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "axiom-paths"));

    [Theory]
    [InlineData("state", "", "", "", "", "state")] // the Host's word wins: AXIOM_MESH_SERVICE_STATE
    [InlineData("", "home", "", "", "", "home/services/chat")] // a home of its own: AXIOM_MESH_HOME
    [InlineData("", "", "local", "", "", "local/Axiom.Mesh/services/chat")] // Windows: %LOCALAPPDATA%
    [InlineData("", "", "", "user", "", "user/.local/share/axiom-mesh/services/chat")] // Linux: the XDG default
    [InlineData("", "", "", "user", "xdg", "xdg/axiom-mesh/services/chat")] // Linux: XDG_DATA_HOME
    [InlineData("", "", "", "", "", null)] // nothing to go by
    public void TheStateDirectoryFollowsTheHostsRules(string state, string home, string local, string user, string xdg, string? expected)
    {
        var variables = new Dictionary<string, string>
        {
            [ServicePaths.StateVariable] = Rooted(state),
            [ServicePaths.HomeVariable] = Rooted(home),
            ["LOCALAPPDATA"] = Rooted(local),
            ["HOME"] = Rooted(user),
            ["XDG_DATA_HOME"] = Rooted(xdg),
        };
        LocalPlatform platform = local.Length > 0 ? LocalPlatform.Windows : user.Length > 0 ? LocalPlatform.Linux : LocalPlatform.Windows;

        string? directory = ServicePaths.StateDirectory("chat", platform, name => variables.GetValueOrDefault(name) is { Length: > 0 } value ? value : null);

        Assert.Equal(expected is null ? null : Path.GetFullPath(Path.Combine(Root, expected)), directory);
    }

    [Theory]
    [InlineData("home", "", "", "home/config.json")] // a home of its own
    [InlineData("", "local", "", "local/Axiom.Mesh/config.json")] // Windows
    [InlineData("", "", "user", "user/.config/axiom-mesh/config.json")] // Linux, the XDG default
    public void TheConfigurationFileIsTheHostsConfigJson(string home, string local, string user, string expected)
    {
        var variables = new Dictionary<string, string> { [ServicePaths.HomeVariable] = Rooted(home), ["LOCALAPPDATA"] = Rooted(local), ["HOME"] = Rooted(user) };
        LocalPlatform platform = user.Length > 0 ? LocalPlatform.Linux : LocalPlatform.Windows;

        string? file = ServicePaths.ConfigFile(platform, name => variables.GetValueOrDefault(name) is { Length: > 0 } value ? value : null);

        Assert.Equal(Path.GetFullPath(Path.Combine(Root, expected)), file);
    }

    private static string Rooted(string relative) => relative.Length == 0 ? string.Empty : Path.Combine(Root, relative);
}
