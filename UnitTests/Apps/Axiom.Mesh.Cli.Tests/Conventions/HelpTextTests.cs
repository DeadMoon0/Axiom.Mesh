using System;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Cli.Tests.Support;

namespace Axiom.Mesh.Cli.Tests.Conventions;

/// <summary>The command table keeps the rules the compiler cannot check: every command and option is explained, and names are unique.</summary>
public sealed class HelpTextTests
{
    [Fact]
    public async Task EveryCommandHasHelpText()
    {
        Assert.NotEmpty(CommandSpecs.All);
        foreach (CommandSpec spec in CommandSpecs.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(spec.Summary), spec.Name + " has no summary");
            Assert.All(spec.Options, o => Assert.False(string.IsNullOrWhiteSpace(o.Help), $"{spec.Name} {o.Name} has no help"));
            Assert.All(spec.Options, o => Assert.StartsWith("--", o.Name, StringComparison.Ordinal));

            (CliExitCode exit, string output, string error) = await Offline.RunAsync(["help", .. spec.Words]);
            Assert.Equal(CliExitCode.Ok, exit);
            Assert.Empty(error);
            Assert.StartsWith("usage: axiom-mesh " + spec.Name, output, StringComparison.Ordinal);
            Assert.Contains(spec.Summary, output, StringComparison.Ordinal);
        }

        (CliExitCode overviewExit, string overview, _) = await Offline.RunAsync("help");
        Assert.Equal(CliExitCode.Ok, overviewExit);
        Assert.All(CommandSpecs.All, s => Assert.Contains(s.Name, overview, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryCommandNameIsUnique_AndEveryOptionOnceWithinItsCommand()
    {
        Assert.Equal(CommandSpecs.All.Count, CommandSpecs.All.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(CommandSpecs.All, s => Assert.Equal(s.Options.Count, s.Options.Select(o => o.Name).Distinct(StringComparer.Ordinal).Count()));
        Assert.All(CommandSpecs.All, s => Assert.True(s.Arguments.Take(s.Arguments.Count - 1).All(a => !a.Variadic), s.Name + ": only the last argument may take the rest"));
    }
}
