using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Host.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>
/// The daemon's configuration (fundamentals plan §2.6, LOCAL-SPEC.md §4.2, §13): every key optional with its default, an
/// unknown key or a wrong value refused at start with the steps out, the external <c>cli</c> service's token generated
/// once from the daemon's randomness and written to a file only the user can read.
/// </summary>
public sealed class HostConfigurationTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task WithoutAFileEveryKeyHasItsDefault_AndTheFirstStartWritesThemWithANewCliToken()
    {
        using var home = new TempHome();
        string file = home.Paths.ConfigFile;

        HostOptions options = await HostConfiguration.LoadAsync(file, new DeterministicEntropy(7), None);

        Assert.Equal(41810, options.Mesh.Port);
        Assert.Null(options.Mesh.Listen);
        Assert.True(options.Mesh.Mdns);
        Assert.True(options.Mesh.Broadcast);
        Assert.Empty(options.Mesh.StaticPeers);
        Assert.Equal(0, options.Store.BudgetBytes);
        Assert.Null(options.Pipe.Name);
        Assert.Empty(options.Pipe.AllowUsers);
        Assert.Empty(options.UserKeyRequests.ConfineTo);
        ServiceSection cli = Assert.Single(options.Services, s => s.Key == "cli").Value;
        Assert.True(cli.External);
        Assert.Equal([1, 2], cli.Kinds.Select(k => (int)k));
        Assert.Equal([1, 2, 3], cli.EntryKinds.Select(k => (int)k));
        Assert.Matches("^[0-9a-f]{64}$", cli.Token);
        Assert.True(File.Exists(file));

        HostOptions again = await HostConfiguration.LoadAsync(file, new DeterministicEntropy(8), None);
        Assert.Equal(cli.Token, again.Services["cli"].Token);
    }

    [Fact]
    public async Task AKeyTheDaemonDoesNotKnowIsRefusedAtStart_NamingItWithTheStepsOut()
    {
        using var home = new TempHome();
        await File.WriteAllTextAsync(home.Paths.ConfigFile, "{ \"mesh\": { \"prot\": 41810 } }");

        var refused = await Assert.ThrowsAsync<MeshConfigurationException>(() => HostConfiguration.LoadAsync(home.Paths.ConfigFile, new DeterministicEntropy(1), None));

        Assert.Contains("prot", refused.Message, StringComparison.Ordinal);
        Assert.Contains("$.mesh", refused.Message, StringComparison.Ordinal);
        Assert.NotEmpty(refused.RecoverySteps);
    }

    [Theory]
    [InlineData("{ \"mesh\": { \"port\": 70000 } }", "mesh.port")] // out of the port range
    [InlineData("{ \"mesh\": { \"listen\": \"not an address\" } }", "mesh.listen")] // not an IP address
    [InlineData("{ \"mesh\": { \"staticPeers\": [\"no-port\"] } }", "mesh.staticPeers")] // not host:port
    [InlineData("{ \"store\": { \"budgetBytes\": -1 } }", "store.budgetBytes")] // negative
    [InlineData("{ \"services\": { \"cli\": { \"external\": true, \"token\": \"xyz\" } } }", "services.cli.token")] // not 64 hex characters
    [InlineData("{ \"services\": { \"cli\": { \"external\": true, \"inProcess\": true } } }", "exactly one")] // two provenances
    [InlineData("{ \"services\": { \"a b\": { \"external\": true } } }", "service name")] // a name outside the character set
    [InlineData("{ \"userKeyRequests\": { \"confineTo\": [\"relative/tool\"] } }", "userKeyRequests.confineTo")] // not an absolute path
    public async Task AValueOutOfRangeIsRefusedAtStart(string json, string named)
    {
        using var home = new TempHome();
        await File.WriteAllTextAsync(home.Paths.ConfigFile, json);

        var refused = await Assert.ThrowsAsync<MeshConfigurationException>(() => HostConfiguration.LoadAsync(home.Paths.ConfigFile, new DeterministicEntropy(1), None));

        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
        Assert.NotEmpty(refused.RecoverySteps);
    }

    [Fact]
    public void TheNodeIsToldTheServicesWithTheirTokensKindsAndTheConfinement()
    {
        HostOptions options = new HostOptions
        {
            Mesh = new MeshSection { Port = 0 },
            Store = new StoreSection { BudgetBytes = 1_000_000 },
            UserKeyRequests = new UserKeyRequestsSection { ConfineTo = ["/usr/bin/axiom-mesh"] },
        } with
        {
            Services = new() { ["cli"] = ServiceSection.DefaultCli with { Token = new string('a', 64) } },
        };

        MeshNodeOptions node = HostConfiguration.NodeOptionsOf(options, "tester", "1.2.3");

        Assert.Equal(0, node.MeshPort);
        Assert.Equal(1_000_000, node.StoreBudgetBytes);
        LocalService cli = Assert.Single(node.Local.Services);
        Assert.Equal(ServiceProvenance.External, cli.Provenance);
        Assert.True(cli.Token.FixedTimeEquals(ServiceToken.TryParseHex(new string('a', 64), out ServiceToken expected) ? expected : default));
        Assert.Equal([PackageKind.Archive, PackageKind.Channel], cli.Kinds);
        Assert.Equal(["/usr/bin/axiom-mesh"], node.Local.ConfineUserKeyRequestsTo);
        Assert.Equal("tester", node.Local.HostUser);
        Assert.Equal("axiom-meshd/1.2.3", node.Local.HostVersion);
    }

    [OsFact("linux")]
    public async Task TheConfigurationFileIsTheUsersAlone_0600()
    {
        if (!OperatingSystem.IsLinux())
            return;
        using var home = new TempHome();
        await HostConfiguration.LoadAsync(home.Paths.ConfigFile, new DeterministicEntropy(1), None);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(home.Paths.ConfigFile));
    }
}
