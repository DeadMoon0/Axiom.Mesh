using System.Collections.Generic;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// The grammar of the sync commands (plan §9.4 item 4): each parses from its good line; <c>sync add</c> takes exactly one of
/// <c>--package</c> and <c>--create</c>, <c>--ignore</c> once per pattern, <c>--symlinks</c> asfile or ignore; the directory of
/// status, scan, conflicts and watch is optional.
/// </summary>
public sealed class SyncCommandLineTests
{
    [Theory]
    [InlineData("sync add ./shared --package Shared", typeof(SyncAddCommand))] // sync add to an Archive
    [InlineData("sync add ./shared --create Shared --label beta --ignore *.tmp --ignore node_modules --symlinks ignore", typeof(SyncAddCommand))] // sync add with every option
    [InlineData("sync remove ./shared", typeof(SyncRemoveCommand))] // sync remove
    [InlineData("sync list --json", typeof(SyncListCommand))] // sync list
    [InlineData("sync status", typeof(SyncStatusCommand))] // sync status of the current directory
    [InlineData("sync status ./shared --json", typeof(SyncStatusCommand))] // sync status of a directory
    [InlineData("sync scan", typeof(SyncScanCommand))] // sync scan
    [InlineData("sync conflicts ./shared --json", typeof(SyncConflictsCommand))] // sync conflicts
    [InlineData("sync watch", typeof(SyncWatchCommand))] // sync watch of every binding
    public void EverySyncCommandParsesFromItsGoodLine(string line, System.Type expected)
    {
        Assert.IsType(expected, Parse(line));
    }

    [Fact]
    public void AddKeepsEveryIgnorePatternInOrder_TheDefaultsAreMainAndSymlinksAsFiles()
    {
        var full = (SyncAddCommand)Parse("sync add d --create Shared --ignore a --ignore *.b --symlinks ignore");
        var plain = (SyncAddCommand)Parse("sync add d --package 3f2a");

        Assert.Equal((null, "Shared", SymlinkPolicy.Ignore), (full.Package, full.Create, full.Symlinks));
        Assert.Equal(["a", "*.b"], full.Ignore);
        Assert.Equal(("3f2a", null, "main", SymlinkPolicy.AsFile), (plain.Package, plain.Create, plain.Label, plain.Symlinks));
    }

    [Theory]
    [InlineData("sync add d")] // neither --package nor --create
    [InlineData("sync add d --package a --create b")] // both
    [InlineData("sync add d --package a --symlinks follow")] // a symlink policy that names nothing
    [InlineData("sync add --package a")] // no directory
    [InlineData("sync remove")] // no directory
    public void AnAddWithoutExactlyOneArchiveOrAValueThatNamesNothingIsAUsageError(string line)
    {
        Assert.IsType<UsageError>(CommandLine.Parse(line.Split(' ')));
    }

    private static Command Parse(string line) =>
        Assert.IsType<ParsedCommand>(CommandLine.Parse(line.Split(' '))).Command;
}
