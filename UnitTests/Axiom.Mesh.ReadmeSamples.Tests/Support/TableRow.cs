using System;
using System.Collections.Generic;

namespace Axiom.Mesh.ReadmeSamples.Tests.Support;

/// <summary>One data row of a markdown table, with the line it stands on.</summary>
/// <param name="Line">The 1-based line number in the document.</param>
/// <param name="Cells">The cells, trimmed, without the outer pipes.</param>
internal sealed record TableRow(int Line, IReadOnlyList<string> Cells)
{
    /// <summary>
    /// Whether the row's last cell (the test that proves it) starts with <c>planned</c>: the promise or requirement is
    /// recorded, and the test it names does not exist yet.
    /// </summary>
    public bool IsPlanned => Cells.Count > 0 && Cells[^1].StartsWith("planned", StringComparison.OrdinalIgnoreCase);
}
