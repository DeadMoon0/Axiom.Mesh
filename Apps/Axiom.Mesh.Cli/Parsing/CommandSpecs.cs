using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>Every command of <c>axiom-mesh</c> (fundamentals plan §2.7), as data: the parser and the help read this table.</summary>
internal static class CommandSpecs
{
    private static readonly OptionSpec JsonOption = OptionSpec.Flag("--json", "print JSON instead of a table");

    /// <summary>Every command, in the order the help lists them.</summary>
    public static IReadOnlyList<CommandSpec> All { get; } =
    [
        Spec(["status"], [], [JsonOption], "whether the Host runs, who this device is, the store and the peers", a => new StatusCommand(a.Flag("--json"))),
        Spec(["version"], [], [], "the version of this tool", _ => new VersionCommand()),
        new(["help"], [new ArgumentSpec("command", Required: false, Variadic: true)], [], "every command, or one command's usage and options", a => new ParsedCommand(new HelpCommand(a.Positionals))),

        Spec(["identity", "create"], [], [OptionSpec.Needed("--user", "name", "your display name"), OptionSpec.Needed("--device", "name", "this device's name")], "create a new user with this device as its first", a => new IdentityCreateCommand(a.Value("--user")!, a.Value("--device")!)),

        Spec(["pair", "show"], [], [OptionSpec.Flag("--device", "show this device's code, to be enrolled by another device of your user")], "show a code for the other side to join, then the string to compare", a => new PairShowCommand(a.Flag("--device"))),
        Spec(["pair", "join"], [ArgumentSpec.Of("code")], [], "join the other side's code: pair the users, or add the shown device to your user", a => new PairJoinCommand(a[0])),
        Spec(["pair", "confirm"], [], [], "confirm that both screens show the same string, and wait for the other side", _ => new PairConfirmCommand()),
        Spec(["pair", "cancel"], [], [], "cancel the current pairing on both sides", _ => new PairCancelCommand()),
        Spec(["pair", "status"], [], [], "the current or last pairing", _ => new PairStatusCommand()),

        Spec(["users"], [], [JsonOption], "every known user, yours included", a => new UsersCommand(a.Flag("--json"))),
        Spec(["devices"], [ArgumentSpec.Optional("user")], [JsonOption], "the devices of a user's Roster, yours by default", a => new DevicesCommand(a.Optional(0), a.Flag("--json"))),
        Spec(["peers"], [], [OptionSpec.Flag("--discovered", "what discovery heard, instead of the peer table"), JsonOption], "the devices this Host knows of and where each stands", a => new PeersCommand(a.Flag("--discovered"), a.Flag("--json"))),
        Spec(["sessions"], [], [JsonOption], "the established sessions and their traffic", a => new SessionsCommand(a.Flag("--json"))),
        Spec(["dial"], [ArgumentSpec.Of("device|ip:port")], [], "dial a known device, or an address once", a => new DialCommand(a[0])),
        Spec(["peer", "add"], [ArgumentSpec.Of("host:port")], [], "add a static peer, dialled now and after every restart", a => new PeerAddCommand(a[0])),
        Spec(["peer", "remove"], [ArgumentSpec.Of("host:port")], [], "remove a static peer", a => new PeerRemoveCommand(a[0])),

        new(["package", "create"], [ArgumentSpec.Of("name")], [OptionSpec.Valued("--kind", "archive|channel", "archive for published folders, channel for messages (default)"), OptionSpec.Valued("--retention", "default|everything|latest", "what other devices keep by default")], "create a Package you own", CreatePackage),
        Spec(["package", "list"], [], [OptionSpec.Flag("--followed", "only the Packages followed here"), JsonOption], "the Packages listed to you or followed here", a => new PackageListCommand(a.Flag("--followed"), a.Flag("--json"))),
        Spec(["package", "show"], [ArgumentSpec.Of("package")], [JsonOption], "one Package and its Grants", a => new PackageShowCommand(a[0], a.Flag("--json"))),
        Spec(["package", "follow"], [ArgumentSpec.Of("package")], [], "replicate a Package here from now on", a => new PackageFollowCommand(a[0])),
        Spec(["package", "unfollow"], [ArgumentSpec.Of("package")], [], "stop replicating a Package", a => new PackageUnfollowCommand(a[0])),
        Spec(["package", "publish"], [ArgumentSpec.Of("dir")], [OptionSpec.Needed("--package", "package", "the Archive to publish to"), OptionSpec.Valued("--label", "branch", "the Version's branch name (default main)")], "publish a folder as a Version of an Archive", a => new PackagePublishCommand(a[0], a.Value("--package")!, a.Value("--label") ?? "main")),
        new(["package", "materialise"], [ArgumentSpec.Of("package"), ArgumentSpec.Of("dir")], [OptionSpec.Valued("--existing", "fail|replace|merge", "what to do when the folder exists (default fail)")], "write the latest Version of a followed Archive into a folder", Materialise),

        Spec(["grant", "set"], [ArgumentSpec.Of("package"), ArgumentSpec.Of("user")], [OptionSpec.Flag("--write", "may write too, not only read")], "let a known user read, or write, a Package you own", a => new GrantSetCommand(a[0], a[1], a.Flag("--write"))),
        Spec(["grant", "revoke"], [ArgumentSpec.Of("package"), ArgumentSpec.Of("user")], [], "withdraw a user's Grant", a => new GrantRevokeCommand(a[0], a[1])),

        Spec(["write"], [ArgumentSpec.Of("package")], [OptionSpec.Needed("--text", "text", "the message")], "write a message to a Channel", a => new WriteCommand(a[0], a.Value("--text")!)),
        new(["history"], [ArgumentSpec.Of("package")], [OptionSpec.Valued("--limit", "n", "how many Entries at most (default 50)"), OptionSpec.Flag("--reverse", "newest first"), JsonOption], "a Package's Entries, oldest first", History),
        Spec(["entry"], [ArgumentSpec.Of("hash")], [JsonOption], "one stored Entry in full", a => new EntryCommand(a[0], a.Flag("--json"))),
        Spec(["object"], [ArgumentSpec.Of("hash")], [OptionSpec.Valued("--out", "file", "write the object's bytes to a file")], "one stored object", a => new ObjectCommand(a[0], a.Value("--out"))),
        Spec(["watch"], [ArgumentSpec.Optional("package")], [], "print every Entry stored from now on, until Ctrl-C", a => new WatchCommand(a.Optional(0))),

        Spec(["store"], [], [JsonOption], "the store's size, budget and what is pinned", a => new StoreCommand(a.Flag("--json"))),
        Spec(["pins"], [], [JsonOption], "every pin and why it holds its object", a => new PinsCommand(a.Flag("--json"))),
        Spec(["pin"], [ArgumentSpec.Of("hash")], [], "keep a stored object whatever the policies say", a => new PinCommand(a[0])),
        Spec(["unpin"], [ArgumentSpec.Of("hash")], [], "remove your pin on an object", a => new UnpinCommand(a[0])),

        new(["host", "install"], [], [], "register the Host to start at logon (not yet available)", NotYet),
        new(["host", "uninstall"], [], [], "remove the Host's start at logon (not yet available)", NotYet),
    ];

    /// <summary>The command a command line names: the one with the longest run of matching words.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The spec, or <see langword="null"/>.</returns>
    public static CommandSpec? Match(IReadOnlyList<string> args) =>
        All.Where(s => s.Words.Count <= args.Count && s.Words.Select((w, i) => args[i] == w).All(m => m))
            .MaxBy(s => s.Words.Count);

    /// <summary>The overview: every command's usage and one line.</summary>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Overview()
    {
        var lines = new List<string> { $"usage: {CliProgram.CommandName} <command> [arguments] [options]", string.Empty };
        int width = All.Max(s => s.Name.Length);
        lines.AddRange(All.Select(s => $"  {s.Name.PadRight(width)}  {s.Summary}"));
        lines.Add(string.Empty);
        lines.Add($"Run '{CliProgram.CommandName} help <command>' for a command's arguments and options.");
        lines.Add("Ids are accepted in full or as a prefix that names one; Packages and users also by their exact name.");
        return lines;
    }

    private static CommandSpec Spec(string[] words, ArgumentSpec[] arguments, OptionSpec[] options, string summary, Func<ParsedArguments, Command> build) =>
        new(words, arguments, options, summary, a => new ParsedCommand(build(a)));

    private static ParseResult CreatePackage(ParsedArguments a)
    {
        PackageKind? kind = a.Value("--kind") switch
        {
            null or "channel" => PackageKind.Channel,
            "archive" => PackageKind.Archive,
            _ => null,
        };
        Retention? retention = a.Value("--retention") switch
        {
            null or "default" => Retention.Default,
            "everything" => Retention.Everything,
            "latest" => Retention.LatestOnly,
            _ => null,
        };
        return kind is null ? a.Problem($"--kind is archive or channel, not '{a.Value("--kind")}'")
            : retention is null ? a.Problem($"--retention is default, everything or latest, not '{a.Value("--retention")}'")
            : new ParsedCommand(new PackageCreateCommand(a[0], kind.Value, retention.Value));
    }

    // LOCAL-SPEC.md §8.6: existing 1 fail, 2 replace, 3 merge.
    private static ParseResult Materialise(ParsedArguments a)
    {
        ExistingPath? existing = a.Value("--existing") switch
        {
            null or "fail" => ExistingPath.Fail,
            "replace" => ExistingPath.Replace,
            "merge" => ExistingPath.Merge,
            _ => null,
        };
        return existing is null
            ? a.Problem($"--existing is fail, replace or merge, not '{a.Value("--existing")}'")
            : new ParsedCommand(new PackageMaterialiseCommand(a[0], a[1], existing.Value));
    }

    private static ParseResult History(ParsedArguments a)
    {
        string? text = a.Value("--limit");
        if (text is null)
            return new ParsedCommand(new HistoryCommand(a[0], HistoryCommand.DefaultLimit, a.Flag("--reverse"), a.Flag("--json")));
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int limit) && limit is >= 1 and <= HistoryCommand.MaxLimit
            ? new ParsedCommand(new HistoryCommand(a[0], limit, a.Flag("--reverse"), a.Flag("--json")))
            : a.Problem($"--limit is a number from 1 to {HistoryCommand.MaxLimit}, not '{text}'");
    }

    private static ParseResult NotYet(ParsedArguments a) =>
        a.Problem($"'{a.Spec.Name}' is not available yet; start the Host yourself with 'axiom-meshd run'");
}
