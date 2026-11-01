using System;
using System.IO;
using Axiom.Mesh.Cli.Tests.Support;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// What the sync commands show (SYNC-SERVICE.md §4): a read-only binding says so, a problem is shown, a watch shows only the
/// binding it watches; the bound folder a path lies in is found by its marker file above it; an older configuration gets the
/// sync service added and a newer one is left alone.
/// </summary>
public sealed class SyncViewTests
{
    [Fact]
    public void AReadOnlyBindingSaysSo_AProblemIsShown_AWatchShowsOnlyItsBinding()
    {
        Binding readOnly = Samples.Bindings[1];

        string status = string.Join("\n", SyncView.Status(readOnly));
        string? watched = SyncView.WatchLine(new BindingEvent { Binding = readOnly }, readOnly.Directory);
        string? other = SyncView.WatchLine(new BindingEvent { Binding = Samples.Bindings[0] }, readOnly.Directory);
        string? conflict = SyncView.WatchLine(new ConflictEvent { Conflict = Samples.Conflicts[0] }, null);

        Assert.Contains("local changes kept (read-only)", status, StringComparison.Ordinal);
        Assert.Contains("Problem", status, StringComparison.Ordinal);
        Assert.StartsWith(readOnly.Directory + ": local changes kept", watched, StringComparison.Ordinal);
        Assert.Null(other);
        Assert.Contains("the other side's copy is docs/report.conflict-d3d2d1d0.txt", conflict, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBoundFolderIsTheNearestOneAboveWithAMarker()
    {
        string root = Path.Combine(Path.GetTempPath(), "shared");
        string marker = Path.Combine(root, SyncProtocol.MarkerName);

        Assert.Equal(root, SyncMarker.Find(Path.Combine(root, "docs", "deep"), path => path == marker));
        Assert.Null(SyncMarker.Find(Path.Combine(root, "docs"), _ => false));
    }

    [Fact]
    public void AnOlderConfigurationGetsTheSyncServiceAdded_ANewerOrForeignOneIsLeftAlone()
    {
        string? added = SyncServiceEntry.Added("""{ "services": { "cli": { "external": true } } }""");

        Assert.Contains("\"sync\": {", added, StringComparison.Ordinal);
        Assert.Contains("\"entryKinds\": [", added, StringComparison.Ordinal);
        Assert.Null(SyncServiceEntry.Added("""{ "services": { "sync": { "spawn": "elsewhere" } } }"""));
        Assert.Null(SyncServiceEntry.Added("""{ "mesh": {} }"""));
        Assert.Null(SyncServiceEntry.Added("not json"));
        Assert.Null(SyncServiceEntry.Added(null));
    }
}
