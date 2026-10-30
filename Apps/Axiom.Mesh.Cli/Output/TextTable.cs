using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Cli;

/// <summary>Aligned text tables: a header row and one row per item, columns two spaces apart, no trailing spaces. Pure.</summary>
internal static class TextTable
{
    /// <summary>The lines of a table.</summary>
    /// <param name="headers">The column headers.</param>
    /// <param name="rows">The rows, each with one cell per header.</param>
    /// <param name="empty">The line shown instead when there is no row.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Lines(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, string empty)
    {
        ArgumentNullException.ThrowIfNull(headers);
        List<IReadOnlyList<string>> all = [.. rows];
        if (all.Count == 0)
            return [empty];

        int[] widths = [.. headers.Select((h, i) => Math.Max(h.Length, all.Max(r => Clean(r[i]).Length)))];
        return [.. new[] { headers }.Concat(all).Select(r => Line(r, widths))];
    }

    /// <summary>The lines of a list of names and values, names aligned.</summary>
    /// <param name="pairs">The names and values, in order.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Pairs(IReadOnlyList<(string Name, string Value)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count == 0)
            return [];
        int width = pairs.Max(p => p.Name.Length);
        return [.. pairs.Select(p => (p.Name.PadRight(width) + "  " + Clean(p.Value)).TrimEnd())];
    }

    private static string Line(IReadOnlyList<string> cells, int[] widths) =>
        string.Join("  ", cells.Select((c, i) => i == cells.Count - 1 ? Clean(c) : Clean(c).PadRight(widths[i]))).TrimEnd();

    // A cell is one line: line breaks and tabs become spaces.
    private static string Clean(string cell) => cell.ReplaceLineEndings(" ").Replace('\t', ' ');
}
