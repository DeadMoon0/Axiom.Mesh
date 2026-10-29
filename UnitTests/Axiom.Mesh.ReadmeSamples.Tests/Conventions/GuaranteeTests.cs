using System;
using System.IO;
using System.Linq;
using Axiom.Mesh.ReadmeSamples.Tests.Support;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.ReadmeSamples.Tests.Conventions;

/// <summary>
/// "A promise without a test is not made": every promise in <c>Documentation/GUARANTEES.md</c> names the test that
/// proves it, and every test a proved promise names exists, so a promise cannot outlive its proof. A promise marked
/// <c>planned</c> names the test that will prove it and is not checked for existence yet.
/// </summary>
public sealed class GuaranteeTests
{
    private static TableRow[] Promises() =>
        [.. DocumentTables.Rows(File.ReadAllText(Path.Combine(Repository.Root, "Documentation", "GUARANTEES.md")), "Promise")];

    [Fact]
    public void EveryPromiseNamesTheTestThatProvesOrWillProveIt()
    {
        TableRow[] promises = Promises();
        string[] unnamed = [.. promises.Where(r => r.Cells.Count != 2 || DocumentTables.References(r.Cells[1]).Count == 0).Select(r => "line " + r.Line)];

        Assert.True(promises.Length >= 10, $"GUARANTEES.md holds {promises.Length} promises; the table format may no longer match.");
        Assert.True(unnamed.Length == 0, "Promises that name no test: " + string.Join(", ", unnamed));
    }

    [Fact]
    public void EveryTestAProvedPromiseNames_Exists()
    {
        var missing = TestCatalog.Missing(Promises().Where(r => !r.IsPlanned).SelectMany(r => DocumentTables.References(r.Cells[^1])));
        Assert.True(missing.Count == 0, "GUARANTEES.md names tests that do not exist: " + string.Join(", ", missing));
    }
}
