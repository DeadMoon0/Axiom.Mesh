using System;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Cli.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// The grammar of fundamentals plan §2.7: every command parses from its good line, and a line that misses an argument or
/// an option it needs, or gives a wrong value, is a usage error that exits 1 before the Host is asked.
/// </summary>
public sealed class CommandLineTests
{
    [Theory]
    [InlineData("status", typeof(StatusCommand))] // status
    [InlineData("status --json", typeof(StatusCommand))] // status as JSON
    [InlineData("version", typeof(VersionCommand))] // version
    [InlineData("help package create", typeof(HelpCommand))] // help on one command
    [InlineData("identity create --user alice --device laptop", typeof(IdentityCreateCommand))] // identity create
    [InlineData("pair show", typeof(PairShowCommand))] // pair show for a user
    [InlineData("pair show --device", typeof(PairShowCommand))] // pair show for this device
    [InlineData("pair join amp1:user:10.0.0.1:41810:KEY", typeof(PairJoinCommand))] // pair join
    [InlineData("pair confirm", typeof(PairConfirmCommand))] // pair confirm
    [InlineData("pair cancel", typeof(PairCancelCommand))] // pair cancel
    [InlineData("pair status", typeof(PairStatusCommand))] // pair status
    [InlineData("users --json", typeof(UsersCommand))] // users
    [InlineData("devices", typeof(DevicesCommand))] // devices of the own user
    [InlineData("devices bob", typeof(DevicesCommand))] // devices of another user
    [InlineData("peers --discovered --json", typeof(PeersCommand))] // peers
    [InlineData("sessions", typeof(SessionsCommand))] // sessions
    [InlineData("dial 10.0.0.2:41810", typeof(DialCommand))] // dial an address
    [InlineData("peer add example.org:41810", typeof(PeerAddCommand))] // peer add
    [InlineData("peer remove example.org:41810", typeof(PeerRemoveCommand))] // peer remove
    [InlineData("package create Photos --kind archive --retention latest", typeof(PackageCreateCommand))] // package create
    [InlineData("package list --followed --json", typeof(PackageListCommand))] // package list
    [InlineData("package show Photos --json", typeof(PackageShowCommand))] // package show
    [InlineData("package follow 3f2a", typeof(PackageFollowCommand))] // package follow
    [InlineData("package unfollow 3f2a", typeof(PackageUnfollowCommand))] // package unfollow
    [InlineData("package publish ./photos --package Photos --label main", typeof(PackagePublishCommand))] // package publish
    [InlineData("package materialise Photos ./out --existing merge", typeof(PackageMaterialiseCommand))] // package materialise
    [InlineData("grant set Photos bob --write", typeof(GrantSetCommand))] // grant set
    [InlineData("grant revoke Photos bob", typeof(GrantRevokeCommand))] // grant revoke
    [InlineData("write Chat --text hello", typeof(WriteCommand))] // write
    [InlineData("history Chat --limit 5 --reverse --json", typeof(HistoryCommand))] // history
    [InlineData("entry 3f2a --json", typeof(EntryCommand))] // entry
    [InlineData("object 3f2a --out a.bin", typeof(ObjectCommand))] // object
    [InlineData("watch", typeof(WatchCommand))] // watch everything
    [InlineData("watch Chat", typeof(WatchCommand))] // watch one Package
    [InlineData("store --json", typeof(StoreCommand))] // store
    [InlineData("pins", typeof(PinsCommand))] // pins
    [InlineData("pin 3f2a", typeof(PinCommand))] // pin
    [InlineData("unpin 3f2a", typeof(UnpinCommand))] // unpin
    public void EveryCommandParsesFromItsGoodLine(string line, Type expected)
    {
        ParseResult parsed = CommandLine.Parse(CliWords.Split(line));

        Assert.IsType(expected, Assert.IsType<ParsedCommand>(parsed).Command);
    }

    [Theory]
    [InlineData("identity create --user alice", "missing --device <name>")] // identity create without a device name
    [InlineData("pair join", "missing <code>")] // pair join without the code
    [InlineData("devices bob alice", "unexpected argument 'alice'")] // devices with two users
    [InlineData("dial", "missing <device|ip:port>")] // dial without a target
    [InlineData("peer add", "missing <host:port>")] // peer add without an address
    [InlineData("peer remove", "missing <host:port>")] // peer remove without an address
    [InlineData("package create", "missing <name>")] // package create without a name
    [InlineData("package create Photos --kind folder", "--kind is archive or channel, not 'folder'")] // a kind that does not exist
    [InlineData("package show", "missing <package>")] // package show without a Package
    [InlineData("package follow", "missing <package>")] // package follow without a Package
    [InlineData("package unfollow", "missing <package>")] // package unfollow without a Package
    [InlineData("package publish ./photos", "missing --package <package>")] // package publish without the Archive
    [InlineData("package materialise Photos", "missing <dir>")] // package materialise without the folder
    [InlineData("package materialise Photos ./out --existing keep", "--existing is fail, replace or merge, not 'keep'")] // an existing mode that does not exist
    [InlineData("grant set Photos", "missing <user>")] // grant set without the user
    [InlineData("grant revoke", "missing <package>")] // grant revoke without the Package
    [InlineData("write Chat", "missing --text <text>")] // write without the text
    [InlineData("write Chat --text", "--text needs a value <text>")] // write with the option but no text
    [InlineData("history", "missing <package>")] // history without the Package
    [InlineData("history Chat --limit 0", "--limit is a number from 1 to 10000, not '0'")] // a limit below 1
    [InlineData("entry", "missing <hash>")] // entry without the hash
    [InlineData("object", "missing <hash>")] // object without the hash
    [InlineData("pin", "missing <hash>")] // pin without the hash
    [InlineData("unpin", "missing <hash>")] // unpin without the hash
    [InlineData("status --verbose", "status has no option --verbose")] // an option the command does not have
    [InlineData("users --json --json", "--json is given twice")] // a flag repeated
    [InlineData("package", "'package' is not a command")] // a group word alone
    [InlineData("frobnicate", "'frobnicate' is not a command")] // an unknown command
    [InlineData("host install", "'host install' is not available yet; start the Host yourself with 'axiom-meshd run'")] // M13's command
    public async Task AMissingArgumentOrAWrongValueIsAUsageErrorThatExits1(string line, string problem)
    {
        (CliExitCode exit, string output, string error) = await Offline.RunAsync([.. CliWords.Split(line)]);

        Assert.Equal(CliExitCode.Usage, exit);
        Assert.Empty(output);
        Assert.Equal("axiom-mesh: " + problem, error.Split('\n')[0]);
        Assert.DoesNotContain(Offline.Pipe, error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValuesReachTheCommand_DefaultsAreChannelMainFiftyAndFail()
    {
        Assert.Equal(new PackageCreateCommand("Chat", PackageKind.Channel, Retention.Default), Command("package create Chat"));
        Assert.Equal(new PackagePublishCommand("./p", "Photos", "main"), Command("package publish ./p --package Photos"));
        Assert.Equal(new HistoryCommand("Chat", 50, false, false), Command("history Chat"));
        Assert.Equal(new PackageMaterialiseCommand("Photos", "out", ExistingPath.Fail), Command("package materialise Photos out"));
        Assert.Equal(new PackageMaterialiseCommand("Photos", "out", ExistingPath.Replace), Command("package materialise Photos out --existing replace"));
        Assert.Equal(new WriteCommand("Chat", "hello there --json"), Command("write Chat --text 'hello there --json'"));
        Assert.Equal(new GrantSetCommand("Photos", "bob", true), Command("grant set Photos bob --write"));
        Assert.Equal(PairingMode.AddDevice, ((PairJoinCommand)Command("pair join amp1:device:10.0.0.1:41810:KEY")).Mode);
        Assert.Equal(PairingMode.JoinUser, ((PairJoinCommand)Command("pair join amp1:user:10.0.0.1:41810:KEY")).Mode);
        Assert.Equal(["package", "create"], ((HelpCommand)Command("package create --help")).Topic);
    }

    private static Command Command(string line) => Assert.IsType<ParsedCommand>(CommandLine.Parse(CliWords.Split(line))).Command;
}
