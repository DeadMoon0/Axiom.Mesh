using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Axiom.Mesh.ReadmeSamples.Tests.Support;

/// <summary>
/// Reads the tables of the documents that name tests (GUARANTEES.md, CONFORMANCE.md, SECURITY.md) as pure functions of
/// their text, so the reading itself is tested against samples.
/// </summary>
internal static partial class DocumentTables
{
    /// <summary>The data rows of every table whose header's first cell is <paramref name="firstHeader"/>.</summary>
    /// <param name="markdown">The document.</param>
    /// <param name="firstHeader">The first header cell, compared ignoring case.</param>
    /// <returns>The rows, in document order.</returns>
    public static IReadOnlyList<TableRow> Rows(string markdown, string firstHeader)
    {
        string[] lines = markdown.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var rows = new List<TableRow>();
        bool inTable = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith('|'))
            {
                inTable = false;
                continue;
            }

            if (i + 1 < lines.Length && IsSeparator(lines[i + 1]))
            {
                inTable = string.Equals(Cells(lines[i])[0], firstHeader, StringComparison.OrdinalIgnoreCase);
                i++;
                continue;
            }

            if (inTable)
                rows.Add(new TableRow(i + 1, Cells(lines[i])));
        }

        return rows;
    }

    /// <summary>Every test a cell names, in backticks as <c>`Project: Class.Method`</c>.</summary>
    /// <param name="cell">The cell's text.</param>
    /// <returns>The references, in order.</returns>
    public static IReadOnlyList<TestReference> References(string cell) =>
        [.. Reference().Matches(cell).Select(m => new TestReference(m.Groups["project"].Value, m.Groups["type"].Value, m.Groups["method"].Value))];

    /// <summary>
    /// The sections a cell cites, as (document, number) pairs: <c>SPEC §5.1, §7</c> is SPEC 5.1 and SPEC 7; a §
    /// without a document belongs to the document named before it.
    /// </summary>
    /// <param name="cell">The cell's text.</param>
    /// <returns>The citations, in order; empty when the cell names no document.</returns>
    public static IReadOnlyList<(string Document, string Number)> Sections(string cell)
    {
        var sections = new List<(string, string)>();
        string? document = null;
        foreach (Match match in Section().Matches(cell))
        {
            if (match.Groups["document"].Success)
                document = match.Groups["document"].Value;
            if (document is not null)
                sections.Add((document, match.Groups["number"].Value));
        }

        return sections;
    }

    /// <summary>Whether a specification has a heading for a section number: <c>## 5. </c> or <c>### 5.1 </c>.</summary>
    /// <param name="specification">The specification's text.</param>
    /// <param name="number">The section number, such as <c>5</c> or <c>5.1</c>.</param>
    /// <returns><see langword="true"/> when the heading exists.</returns>
    public static bool HasSection(string specification, string number)
    {
        string heading = number.Contains('.', StringComparison.Ordinal)
            ? @"^#{3,4} " + Regex.Escape(number) + " "
            : @"^## " + Regex.Escape(number) + @"\. ";
        return Regex.IsMatch(specification, heading, RegexOptions.Multiline);
    }

    private static bool IsSeparator(string line) => SeparatorRow().IsMatch(line);

    private static string[] Cells(string line) =>
        [.. line.Trim().Trim('|').Split('|').Select(c => c.Trim())];

    [GeneratedRegex(@"^\|(\s*:?-+:?\s*\|)+\s*$")]
    private static partial Regex SeparatorRow();

    [GeneratedRegex(@"`(?<project>[A-Z][A-Za-z.]*): (?<type>[A-Z][A-Za-z0-9]*)\.(?<method>[A-Z][A-Za-z0-9_]*)`")]
    private static partial Regex Reference();

    [GeneratedRegex(@"(?:(?<document>SPEC|LOCAL)\s+)?§(?<number>\d+(?:\.\d+)*)")]
    private static partial Regex Section();
}
