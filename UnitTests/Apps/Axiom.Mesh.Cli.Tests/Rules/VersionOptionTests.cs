using System;
using System.IO;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>What the command line answers before it reaches the Host: its version, or how to call it.</summary>
public sealed class VersionOptionTests
{
    private const string Version = "0.1.0+abc123";

    [Fact]
    public void VersionPrintsTheToolsVersionAloneAndSucceeds()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        CliExitCode exit = CliProgram.Run(["--version"], output, error, Version);

        Assert.Equal(CliExitCode.Ok, exit);
        Assert.Equal(Version + Environment.NewLine, output.ToString());
        Assert.Empty(error.ToString());
    }

    [Theory]
    [InlineData] // no command at all
    [InlineData("--versoin")] // a misspelt option
    [InlineData("--version", "--json")] // the option with an argument it does not take
    public void AnythingElsePrintsTheUsageToStandardErrorAndExits1(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        CliExitCode exit = CliProgram.Run(args, output, error, Version);

        Assert.Equal(CliExitCode.Usage, exit);
        Assert.Equal(1, (int)exit);
        Assert.Empty(output.ToString());
        Assert.StartsWith("usage: axiom-mesh --version", error.ToString(), StringComparison.Ordinal);
    }
}
