using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Axiom.Mesh.ReadmeSamples.Tests.Support;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.ReadmeSamples.Tests.Conventions;

/// <summary>
/// <c>Documentation/CONFORMANCE.md</c> maps every requirement of the specifications to the test that proves it: every
/// row cites a section that exists, and every test a row names (unless the row says <c>planned</c>) exists.
/// </summary>
public sealed class ConformanceDocTests
{
    private static readonly Dictionary<string, string> Specifications = new(StringComparer.Ordinal)
    {
        ["SPEC"] = "SPEC.md",
        ["LOCAL"] = "LOCAL-SPEC.md",
        ["PAIR"] = "PAIR-SPEC.md",
        ["PAYLOADS"] = "PAYLOADS.md",
    };

    private static TableRow[] Requirements() =>
        [.. DocumentTables.Rows(File.ReadAllText(Path.Combine(Repository.Root, "Documentation", "CONFORMANCE.md")), "Section")];

    [Fact]
    public void EveryRowHasSectionRequirementBehaviourAndTest()
    {
        TableRow[] rows = Requirements();
        string[] malformed = [.. rows.Where(r => r.Cells.Count != 4 || r.Cells.Any(string.IsNullOrWhiteSpace)).Select(r => "line " + r.Line)];

        Assert.True(rows.Length >= 20, $"CONFORMANCE.md holds {rows.Length} rows; the table format may no longer match.");
        Assert.True(malformed.Length == 0, "Rows without four filled cells: " + string.Join(", ", malformed));
    }

    [Fact]
    public void EveryRowCitesASectionOfTheSpecificationsThatExists()
    {
        var texts = Specifications.ToDictionary(s => s.Key, s => File.ReadAllText(Path.Combine(Repository.Root, "Documentation", s.Value)), StringComparer.Ordinal);
        var wrong = new List<string>();
        foreach (TableRow row in Requirements())
        {
            var sections = DocumentTables.Sections(row.Cells[0]);
            if (sections.Count == 0)
                wrong.Add($"line {row.Line} cites no section");
            wrong.AddRange(sections.Where(s => !DocumentTables.HasSection(texts[s.Document], s.Number)).Select(s => $"line {row.Line}: {s.Document} §{s.Number}"));
        }

        Assert.True(wrong.Count == 0, "Citations of sections that do not exist: " + string.Join(", ", wrong));
    }

    [Fact]
    public void EveryTestARowNames_ExistsUnlessTheRowIsPlanned()
    {
        var missing = TestCatalog.Missing(Requirements().Where(r => !r.IsPlanned).SelectMany(r => DocumentTables.References(r.Cells[^1])));
        Assert.True(missing.Count == 0, "CONFORMANCE.md names tests that do not exist: " + string.Join(", ", missing));
    }
}
