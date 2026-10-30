using System;
using System.Threading.Tasks;
using Axiom.Mesh.Cli.Tests.Support;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>What the command line answers before it reaches the Host: its version, or how to call it.</summary>
public sealed class VersionOptionTests
{
    [Theory]
    [InlineData("--version")] // the option
    [InlineData("version")] // the command
    public async Task VersionPrintsTheToolsVersionAloneAndSucceeds(string word)
    {
        (CliExitCode exit, string output, string error) = await Offline.RunAsync(word);

        Assert.Equal(CliExitCode.Ok, exit);
        Assert.Equal(Offline.Environment.Version + "\n", output);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData] // no command at all
    [InlineData("--versoin")] // a misspelt option
    [InlineData("--version", "--json")] // the option with an argument it does not take
    public async Task AnythingElsePrintsTheUsageToStandardErrorAndExits1(params string[] args)
    {
        (CliExitCode exit, string output, string error) = await Offline.RunAsync(args);

        Assert.Equal(CliExitCode.Usage, exit);
        Assert.Equal(1, (int)exit);
        Assert.Empty(output);
        Assert.StartsWith("axiom-mesh: ", error, StringComparison.Ordinal);
        Assert.Contains("usage: axiom-mesh", error, StringComparison.Ordinal);
    }
}
