using System;
using System.IO;
using System.Linq;
using Axiom.Mesh.Cli.Tests.Support;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Cli.Tests.Regression;

/// <summary>
/// The output of <c>status</c>, <c>package list</c>, <c>history</c>, <c>peers</c>, <c>pair join</c> (the discovered devices), <c>services</c>, <c>chat list</c>, <c>chat show</c>, <c>sync list</c>, <c>sync status</c> and <c>sync conflicts</c>, as text and as <c>--json</c>, for fixed
/// records: what scripts and people read today. The snapshots live in <c>Regression/Snapshots</c>.
/// </summary>
public sealed class OutputSnapshotTests
{
    private const string Host = @"\\.\pipe\axiom-mesh";

    // Damage: a column renamed, moved or realigned, a JSON property renamed or retyped, an id no longer cut to 12 hex
    // digits or a time no longer UTC; every script that reads the command line's output would break unseen.
    [Theory]
    [InlineData("status.txt")] // status as text
    [InlineData("status.json")] // status --json
    [InlineData("package-list.txt")] // package list as text
    [InlineData("package-list.json")] // package list --json
    [InlineData("history.txt")] // history as text, a withdrawn message among them
    [InlineData("history.json")] // history --json
    [InlineData("peers.txt")] // peers as text
    [InlineData("peers.json")] // peers --json
    [InlineData("pair-join.txt")] // pair join without a target: the discovered devices a pairing could be with
    [InlineData("services.txt")] // services as text
    [InlineData("services.json")] // services --json
    [InlineData("chat-list.txt")] // chat list as text
    [InlineData("chat-list.json")] // chat list --json
    [InlineData("chat-show.txt")] // chat show as text, a withdrawn message and a reply among them
    [InlineData("chat-show.json")] // chat show --json, without the local store time
    [InlineData("sync-list.txt")] // sync list as text, a read-only binding among them
    [InlineData("sync-list.json")] // sync list --json
    [InlineData("sync-status.txt")] // sync status of one binding
    [InlineData("sync-conflicts.txt")] // sync conflicts as text
    public void TheOutputOfTheListsIsUnchanged(string snapshot)
    {
        string actual = Render(snapshot);
        string expected = File.ReadAllText(Path.Combine(Repository.ProjectFolder("Axiom.Mesh.Cli.Tests"), "Regression", "Snapshots", snapshot)).ReplaceLineEndings("\n");

        Assert.Equal(expected, actual);
    }

    private static string Render(string snapshot) => snapshot switch
    {
        "status.txt" => Lines(StatusView.Text(Host, Samples.Ack, Samples.Identity, Samples.Store, Samples.Peers)),
        "status.json" => StatusView.Json(Host, Samples.Ack, Samples.Identity, Samples.Store, Samples.Peers) + "\n",
        "package-list.txt" => Lines(PackagesView.Text(Samples.Packages)),
        "package-list.json" => PackagesView.Json(Samples.Packages) + "\n",
        "history.txt" => Lines(HistoryView.Text(Samples.History)),
        "history.json" => HistoryView.Json(Samples.History) + "\n",
        "peers.txt" => Lines(PeersView.Text(Samples.Peers)),
        "peers.json" => PeersView.Json(Samples.Peers) + "\n",
        "pair-join.txt" => Lines(PairingView.Discovered([.. Samples.Discovered.Where(PairingView.IsJoinable)])),
        "services.txt" => Lines(ServicesView.Text(Samples.Services)),
        "services.json" => ServicesView.Json(Samples.Services) + "\n",
        "chat-list.txt" => Lines(ChatView.Channels(Samples.Channels)),
        "chat-list.json" => ChatView.ChannelsJson(Samples.Channels) + "\n",
        "chat-show.txt" => Lines(ChatView.Timeline(Samples.Timeline)),
        "chat-show.json" => ChatView.TimelineJson(Samples.Timeline) + "\n",
        "sync-list.txt" => Lines(SyncView.Bindings(Samples.Bindings)),
        "sync-list.json" => SyncView.BindingsJson(Samples.Bindings) + "\n",
        "sync-status.txt" => Lines(SyncView.Status(Samples.Bindings[1])),
        "sync-conflicts.txt" => Lines(SyncView.Conflicts(Samples.Conflicts)),
        _ => throw new ArgumentException("No such snapshot: " + snapshot, nameof(snapshot)),
    };

    private static string Lines(System.Collections.Generic.IReadOnlyList<string> lines) => string.Join("\n", lines) + "\n";
}
