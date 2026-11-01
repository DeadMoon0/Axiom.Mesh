using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Sync.Client.Tests.Conventions;

/// <summary>
/// <c>Documentation/SYNC-SERVICE.md</c> and the Client library describe the same namespace: every row of its method, event and
/// status tables names a name and a type the library has, and every name and code of the library has its row.
/// </summary>
public sealed class SyncServiceDocTests
{
    private static readonly string Document = File.ReadAllText(Path.Combine(Repository.Root, "Documentation", "SYNC-SERVICE.md"));

    [Fact]
    public void EveryMethodEventAndStatusRowOfTheDocumentNamesWhatTheLibraryHas_AndTheLibraryHasNoNameWithoutARow()
    {
        HashSet<string> types = [.. typeof(SyncCall).Assembly.GetExportedTypes().Select(t => t.Name)];
        IReadOnlyList<string[]> methods = Rows("Method");
        IReadOnlyList<string[]> events = Rows("Event");
        IReadOnlyList<string[]> statuses = Rows("Code");

        Assert.Equal(Names(typeof(SyncMethods)), methods.Select(r => Unquote(r[0])).Order());
        Assert.All(methods, r => Assert.True(types.Contains(Unquote(r[1])) && types.Contains(Unquote(r[2])), string.Join(" | ", r)));
        Assert.Equal(Names(typeof(SyncEvents)), events.Select(r => Unquote(r[0])).Order());
        Assert.All(events, r => Assert.True(types.Contains(Unquote(r[1])), string.Join(" | ", r)));
        SyncStatus[] codes = [.. Enum.GetValues<SyncStatus>().Where(s => s != SyncStatus.None)];
        Assert.Equal(codes.Select(c => ((int)c).ToString(System.Globalization.CultureInfo.InvariantCulture)), statuses.Select(r => r[0]));
        Assert.Equal(codes.Select(c => c.ToString()), statuses.Select(r => Unquote(r[1])));
    }

    private static IEnumerable<string> Names(Type table) =>
        table.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!).Order();

    private static string Unquote(string cell) => cell.Trim().Trim('`');

    // The rows of the table whose first header cell is the given one.
    private static IReadOnlyList<string[]> Rows(string firstHeader)
    {
        string[] lines = Document.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        int header = Array.FindIndex(lines, l => Regex.IsMatch(l, $@"^\|\s*{firstHeader}\s*\|"));
        Assert.True(header >= 0, $"SYNC-SERVICE.md has no table headed '{firstHeader}'.");
        return [.. lines.Skip(header + 2).TakeWhile(l => l.StartsWith('|')).Select(l => l.Trim('|').Split('|').Select(c => c.Trim()).ToArray())];
    }
}
