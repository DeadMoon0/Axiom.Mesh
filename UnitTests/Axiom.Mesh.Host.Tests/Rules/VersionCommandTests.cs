using System;
using System.IO;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>What the daemon answers before it runs anything: its version, or how to call it.</summary>
public sealed class VersionCommandTests
{
    private const string Version = "0.1.0+abc123";

    [Fact]
    public void VersionPrintsTheDaemonsVersionAloneAndSucceeds()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        HostExitCode exit = HostProgram.Run(["version"], output, error, Version);

        Assert.Equal(HostExitCode.Ok, exit);
        Assert.Equal(Version + Environment.NewLine, output.ToString());
        Assert.Empty(error.ToString());
    }

    [Theory]
    [InlineData] // no command at all
    [InlineData("run-everything")] // a command the daemon does not know
    [InlineData("version", "--verbose")] // a known command with an argument it does not take
    public void AnythingElsePrintsTheUsageToStandardErrorAndFailsAsUsage(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        HostExitCode exit = HostProgram.Run(args, output, error, Version);

        Assert.Equal(HostExitCode.Usage, exit);
        Assert.Empty(output.ToString());
        Assert.StartsWith("usage: axiom-meshd version", error.ToString(), StringComparison.Ordinal);
    }
}
