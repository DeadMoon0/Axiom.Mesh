using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Local.Tests.Support;

/// <summary>Reads the markdown tables of LOCAL-SPEC.md, so conventions can hold the code to its registries.</summary>
internal static class SpecTables
{
    /// <summary>The text of LOCAL-SPEC.md.</summary>
    public static string LocalSpec { get; } = File.ReadAllText(Path.Combine(Repository.Root, "Documentation", "LOCAL-SPEC.md"));

    /// <summary>
    /// The data rows of the <paramref name="index"/>th table (0-based) between the heading that starts with
    /// <paramref name="heading"/> and the next heading.
    /// </summary>
    public static IReadOnlyList<string[]> Rows(string heading, int index)
    {
        string[] lines = [.. LocalSpec.Split('\n').Select(l => l.TrimEnd('\r'))];
        int start = Array.FindIndex(lines, l => l.StartsWith(heading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"LOCAL-SPEC.md has no heading starting with '{heading}'.");
        int end = Array.FindIndex(lines, start + 1, l => l.StartsWith('#'));
        end = end < 0 ? lines.Length : end;

        var tables = new List<List<string[]>>();
        for (int i = start; i + 1 < end; i++)
        {
            if (lines[i].StartsWith('|') && lines[i + 1].StartsWith("|---", StringComparison.Ordinal) && (i == 0 || !lines[i - 1].StartsWith('|')))
                tables.Add([.. lines.Skip(i + 2).Take(end - i - 2).TakeWhile(l => l.StartsWith('|')).Select(Cells)]);
        }

        Assert.True(index < tables.Count, $"The section '{heading}' has {tables.Count} tables, not {index + 1}.");
        return tables[index];
    }

    /// <summary>The rows of a registry table whose first cell is a number: (value, name).</summary>
    public static IReadOnlyList<(int Value, string Name)> Registry(string heading, int index, int nameColumn) =>
        [.. Rows(heading, index)
            .Where(r => int.TryParse(r[0], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(r => (int.Parse(r[0], CultureInfo.InvariantCulture), r[nameColumn].Trim('`')))];

    private static string[] Cells(string line) => [.. line.Trim().Trim('|').Split('|').Select(c => c.Trim())];
}
