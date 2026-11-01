using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Axiom.Mesh.Sync.Tests.Support.Facts;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SPEC.md §9.5: the other side's copy is named <c>&lt;name&gt;.conflict-&lt;first 8 hex of the other deviceId&gt;</c> before any
/// extension, and a taken name gets <c>-2</c>, <c>-3</c>, … appended.
/// </summary>
public sealed class ConflictNameTests
{
    private static readonly string Hex8 = Desktop.Value.ToHex()[..8];

    [Theory]
    [InlineData("report.txt", "report.conflict-{0}.txt")] // the extension stays last
    [InlineData("archive.tar.gz", "archive.conflict-{0}.tar.gz")] // the extension starts at the first dot
    [InlineData("Makefile", "Makefile.conflict-{0}")] // no extension: the tag goes last
    [InlineData(".bashrc", ".bashrc.conflict-{0}")] // a leading dot starts no extension
    [InlineData(".config.json", ".config.conflict-{0}.json")] // but a later one does
    public void TheTagGoesBeforeTheExtension(string name, string expected)
    {
        Assert.Equal(string.Format(System.Globalization.CultureInfo.InvariantCulture, expected, Hex8), ConflictName.Of(name, Desktop, new HashSet<string>()));
    }

    [Fact]
    public void ATakenNameGetsMinus2ThenMinus3_ComparedWithoutCase()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { $"REPORT.CONFLICT-{Hex8.ToUpperInvariant()}.TXT" };

        string second = ConflictName.Of("report.txt", Desktop, taken);
        taken.Add(second);
        string third = ConflictName.Of("report.txt", Desktop, taken);

        Assert.Equal(($"report.conflict-{Hex8}-2.txt", $"report.conflict-{Hex8}-3.txt"), (second, third));
    }

    [Fact]
    public void TheNameStaysWithin255Bytes_TheStemShortenedFirst()
    {
        string name = new string('ä', 120) + ".txt";

        string copy = ConflictName.Of(name, Desktop, new HashSet<string>());

        Assert.True(Encoding.UTF8.GetByteCount(copy) <= Limits.MaxTreeNameLength);
        Assert.EndsWith($".conflict-{Hex8}.txt", copy, StringComparison.Ordinal);
        Assert.StartsWith("ää", copy, StringComparison.Ordinal);
        Assert.True(copy.Count(c => c == 'ä') < 120);
    }
}
