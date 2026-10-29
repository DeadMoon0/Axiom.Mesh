using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Protocol.Tests.Support;

/// <summary>The markdown tables of one section of a document under <c>Documentation/</c>, so conventions can hold the code to them.</summary>
internal static class SectionTables
{
    /// <summary>
    /// The data rows of every table between the heading that starts with <paramref name="heading"/> and the next heading,
    /// one list per table, each row's cells trimmed.
    /// </summary>
    /// <param name="document">The document's file name, such as <c>SPEC.md</c>.</param>
    /// <param name="heading">The heading line's start, such as <c>### 14.1 </c>.</param>
    /// <returns>The tables in order.</returns>
    public static IReadOnlyList<IReadOnlyList<string[]>> Of(string document, string heading)
    {
        string[] lines = [.. File.ReadAllText(Path.Combine(Repository.Root, "Documentation", document)).Split('\n').Select(l => l.TrimEnd('\r'))];
        int start = Array.FindIndex(lines, l => l.StartsWith(heading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"{document} has no heading starting with '{heading}'.");

        var tables = new List<IReadOnlyList<string[]>>();
        List<string[]>? rows = null;
        for (int i = start + 1; i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            if (!lines[i].StartsWith('|'))
            {
                rows = null;
                continue;
            }

            if (rows is null)
            {
                rows = [];
                tables.Add(rows);
                i++; // the separator row under the header
                continue;
            }

            rows.Add([.. lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim())]);
        }

        return tables;
    }
}
