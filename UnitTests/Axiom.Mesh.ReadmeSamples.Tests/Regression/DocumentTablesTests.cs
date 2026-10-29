using Axiom.Mesh.ReadmeSamples.Tests.Support;

namespace Axiom.Mesh.ReadmeSamples.Tests.Regression;

/// <summary>The reading of GUARANTEES.md and CONFORMANCE.md tables, which the document checks rely on.</summary>
public sealed class DocumentTablesTests
{
    private const string Sample =
        "# Doc\n\n" +
        "| Promise | Proved by |\n" +
        "|---|---|\n" +
        "| One. | `Model.Tests: FooTests.Works`, `Shared: BarTests.AlsoWorks` |\n" +
        "| Two. | planned: `Core.Tests: BazTests.Later` |\n\n" +
        "| Other | Table |\n" +
        "|---|---|\n" +
        "| not | read |\n";

    // Damage: a changed table layout makes the checks read zero rows, and an empty document passes every check.
    [Fact]
    public void RowsOfTheNamedTableAreRead_WithTheirLines_AndPlannedRowsAreMarked()
    {
        var rows = DocumentTables.Rows(Sample, "Promise");

        Assert.Equal(2, rows.Count);
        Assert.Equal(5, rows[0].Line);
        Assert.False(rows[0].IsPlanned);
        Assert.True(rows[1].IsPlanned);
        Assert.Equal(
            [new TestReference("Model.Tests", "FooTests", "Works"), new TestReference("Shared", "BarTests", "AlsoWorks")],
            DocumentTables.References(rows[0].Cells[1]));
    }

    // Damage: a citation of a section that was renumbered or never existed goes unnoticed.
    [Fact]
    public void ASectionCellYieldsEveryCitation_AndTheHeadingCheckTellsTopLevelFromSubsections()
    {
        Assert.Equal([("SPEC", "5.1"), ("SPEC", "7"), ("LOCAL", "9.4")], DocumentTables.Sections("SPEC §5.1, §7; LOCAL §9.4"));

        const string Specification = "## 5. Framing\n\n### 5.1 Frames\n";
        Assert.True(DocumentTables.HasSection(Specification, "5"));
        Assert.True(DocumentTables.HasSection(Specification, "5.1"));
        Assert.False(DocumentTables.HasSection(Specification, "5.2"));
        Assert.False(DocumentTables.HasSection(Specification, "1"));
    }
}
