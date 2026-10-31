using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Axiom.Mesh.ReadmeSamples.Tests.Support;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.ReadmeSamples.Tests.Conventions;

/// <summary>
/// ARCHITECTURE.md rule 9 for <c>Documentation/SECURITY.md</c>: every row of its tables (threats, checks, keys, the pipe,
/// the TLS parameters) names in its last cell the test or the workflow that proves it, and every test and workflow it names
/// exists. A security claim cannot outlive its proof.
/// </summary>
public sealed partial class SecurityDocTests
{
    // The first header of each table the document holds; each table's last column says what proves the row.
    private static readonly string[] Tables = ["Threat", "Check", "Key fact", "Pipe fact", "Release fact", "Parameter"];

    private static string Document => File.ReadAllText(Path.Combine(Repository.Root, "Documentation", "SECURITY.md"));

    [Fact]
    public void EveryRowNamesATestOrAWorkflow()
    {
        var rows = Tables.SelectMany(t => DocumentTables.Rows(Document, t).Select(r => (Table: t, Row: r))).ToList();
        string[] unproved = [.. rows
            .Where(r => DocumentTables.References(r.Row.Cells[^1]).Count == 0 && Workflows(r.Row.Cells[^1]).Count == 0)
            .Select(r => $"{r.Table} table, line {r.Row.Line}")];

        foreach (string table in Tables)
            Assert.True(DocumentTables.Rows(Document, table).Count > 0, $"SECURITY.md has no table headed '{table}'; the document and this test disagree.");
        Assert.True(unproved.Length == 0, "Rows that name neither a test nor a workflow: " + string.Join(", ", unproved));
    }

    [Fact]
    public void EveryTestAndWorkflowItNames_Exists()
    {
        var cells = Tables.SelectMany(t => DocumentTables.Rows(Document, t)).Select(r => r.Cells[^1]).ToList();
        var missing = new List<string>(TestCatalog.Missing(cells.SelectMany(DocumentTables.References)));
        missing.AddRange(cells.SelectMany(Workflows).Distinct().Where(w => !File.Exists(Path.Combine(Repository.Root, ".github", "workflows", w))));

        Assert.True(missing.Count == 0, "SECURITY.md names tests or workflows that do not exist: " + string.Join(", ", missing));
    }

    private static IReadOnlyList<string> Workflows(string cell) => [.. WorkflowName().Matches(cell).Select(m => m.Groups[1].Value)];

    [GeneratedRegex(@"`([a-z0-9-]+\.yml)`")]
    private static partial Regex WorkflowName();
}
