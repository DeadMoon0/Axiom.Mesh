using System;
using System.IO;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>
/// S-D11, where a spawned service's executable is: an absolute path as written, a bare file name beside the daemon with
/// <c>.exe</c> added on Windows; anything else (a relative path with a directory) is refused by the configuration check.
/// </summary>
public sealed class SpawnPathRuleTests
{
    private static readonly string Daemon = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "axiom-mesh-bin"));

    [Theory]
    [InlineData("axiom-mesh-chat", "windows", "axiom-mesh-chat.exe")] // the default entry on Windows
    [InlineData("axiom-mesh-chat", "linux", "axiom-mesh-chat")] // and on Linux
    [InlineData("chat.exe", "windows", "chat.exe")] // an extension is kept
    public void ABareNameIsTheFileBesideTheDaemon(string spawn, string os, string file)
    {
        string resolved = SpawnPathRule.Resolve(spawn, Daemon, os == "windows" ? LocalPlatform.Windows : LocalPlatform.Linux);

        Assert.Equal(Path.Combine(Daemon, file), resolved);
    }

    // An absolute path is the executable as written.
    [Fact]
    public void AnAbsolutePathIsTakenAsWritten()
    {
        string absolute = Path.Combine(Daemon, "tools", "dotnet");

        Assert.Equal(absolute, SpawnPathRule.Resolve(absolute, Path.GetTempPath(), LocalPlatform.Linux));
    }

    [Theory]
    [InlineData("tools/chat", false)] // a relative path with a directory: neither form
    [InlineData("..", false)] // not a file name
    [InlineData("", false)] // nothing
    [InlineData("axiom-mesh-sync", true)] // a bare name
    public void OnlyAnAbsolutePathOrABareNameIsASpawnValue(string spawn, bool valid)
    {
        Assert.Equal(valid, SpawnPathRule.IsAbsoluteOrBareName(spawn));
        if (!valid)
            Assert.Throws<ArgumentException>(() => SpawnPathRule.Resolve(spawn, Daemon, LocalPlatform.Linux));
    }
}
