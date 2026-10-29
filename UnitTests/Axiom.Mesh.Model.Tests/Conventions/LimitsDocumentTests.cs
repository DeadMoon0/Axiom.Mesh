using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Conventions;

/// <summary>
/// ARCHITECTURE.md rule 5: every limit is a named constant with the specification's value. <c>Documentation/LIMITS.md</c>
/// names the constant of every bound, and <see cref="Limits"/> holds exactly the constants it names; the values
/// themselves are checked against the specifications by <c>Rules/LimitsTests</c>.
/// </summary>
public sealed class LimitsDocumentTests
{
    // Bounds the specifications do not fix to a number: the store budget is configuration, and "their maximum" refers
    // to each list's own bound.
    private static readonly string[] NotAConstant = ["pinned bytes", "lists without a cursor"];

    [Fact]
    public void EveryLimitOfTheSpecIsANamedConstantWithTheSpecsValue()
    {
        string[][] rows = [.. SpecTables.AllRows(SpecTables.Document("LIMITS.md"), "Limit")];
        Assert.True(rows.Length >= 50, $"LIMITS.md holds {rows.Length} rows; the table format may no longer match.");

        string[] declared = [.. typeof(Limits).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => f.Name)];
        string[] named = [.. rows.SelectMany(r => Regex.Matches(r[^1], @"`Limits\.(\w+)`").Select(m => m.Groups[1].Value)).Distinct()];

        string[] unnamedRows = [.. rows.Where(r => r[^1] == "—" && !NotAConstant.Contains(r[0], StringComparer.Ordinal)).Select(r => r[0])];
        Assert.True(unnamedRows.Length == 0, "LIMITS.md rows without a constant: " + string.Join(", ", unnamedRows));
        string[] missing = [.. named.Except(declared)];
        string[] undocumented = [.. declared.Except(named)];
        Assert.True(missing.Length == 0, "LIMITS.md names constants Limits lacks: " + string.Join(", ", missing));
        Assert.True(undocumented.Length == 0, "Limits constants LIMITS.md does not name: " + string.Join(", ", undocumented));
    }
}
