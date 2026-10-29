using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Tests.Support;

/// <summary>Reads the markdown tables of the documents, so conventions can hold the code to them.</summary>
internal static class SpecTables
{
    /// <summary>The text of a document under <c>Documentation/</c>.</summary>
    public static string Document(string name) => File.ReadAllText(Path.Combine(Repository.Root, "Documentation", name));

    /// <summary>The data rows of the first table under the heading that starts with <paramref name="heading"/>.</summary>
    /// <param name="markdown">The document.</param>
    /// <param name="heading">The heading line's start, such as <c>### 14.6 </c>.</param>
    /// <returns>Each row's cells, trimmed.</returns>
    public static IReadOnlyList<string[]> Rows(string markdown, string heading)
    {
        string[] lines = [.. markdown.Split('\n').Select(l => l.TrimEnd('\r'))];
        int start = Array.FindIndex(lines, l => l.StartsWith(heading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"No heading starting with '{heading}'.");
        int header = Array.FindIndex(lines, start, l => l.StartsWith('|'));
        return [.. lines.Skip(header + 2).TakeWhile(l => l.StartsWith('|')).Select(Cells)];
    }

    /// <summary>The data rows of every table in the document whose first header cell is <paramref name="firstHeader"/>.</summary>
    public static IReadOnlyList<string[]> AllRows(string markdown, string firstHeader)
    {
        string[] lines = [.. markdown.Split('\n').Select(l => l.TrimEnd('\r'))];
        var rows = new List<string[]>();
        for (int i = 0; i + 1 < lines.Length; i++)
        {
            if (lines[i].StartsWith('|') && Cells(lines[i])[0] == firstHeader && lines[i + 1].StartsWith("|---", StringComparison.Ordinal))
                rows.AddRange(lines.Skip(i + 2).TakeWhile(l => l.StartsWith('|')).Select(Cells));
        }

        return rows;
    }

    private static string[] Cells(string line) => [.. line.Trim().Trim('|').Split('|').Select(c => c.Trim())];
}
