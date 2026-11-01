using System;
using System.IO;
using Axiom.Mesh.Cli.Tests.Support;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Cli.Tests.Regression;

/// <summary>
/// The output of <c>status</c>, <c>package list</c>, <c>history</c>, <c>peers</c> and <c>services</c>, as text and as <c>--json</c>, for fixed
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
    [InlineData("services.txt")] // services as text
    [InlineData("services.json")] // services --json
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
        "services.txt" => Lines(ServicesView.Text(Samples.Services)),
        "services.json" => ServicesView.Json(Samples.Services) + "\n",
        _ => throw new ArgumentException("No such snapshot: " + snapshot, nameof(snapshot)),
    };

    private static string Lines(System.Collections.Generic.IReadOnlyList<string> lines) => string.Join("\n", lines) + "\n";
}
