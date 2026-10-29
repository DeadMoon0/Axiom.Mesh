using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Axiom.Mesh.Tests.Shared;

/// <summary>
/// The test-suite conventions as pure functions of one source file's text, so the conventions themselves can be tested
/// against samples. Each returns the 1-based numbers of the lines that break the rule.
/// </summary>
internal static partial class TestSourceRules
{
    private const string DamageMarker = "// Damage:";

    /// <summary>
    /// Lines that wait by the wall clock. A sleep or delay makes a test slow on a quiet machine and flaky on a busy one;
    /// tests wait on a subscription, a task or a fake clock instead.
    /// </summary>
    /// <param name="source">The text of one C# file.</param>
    /// <returns>The offending line numbers.</returns>
    public static IReadOnlyList<int> SleepsAndDelays(string source) =>
        [.. Lines(source)
            .Select((line, index) => (Code: CodeOf(line), Number: index + 1))
            .Where(l => WallClockWait().IsMatch(l.Code))
            .Select(l => l.Number)];

    /// <summary>
    /// Test methods with no <c>// Damage:</c> line. A test owns its leading block (comments and attributes directly
    /// above it) and everything after it up to the next test's leading block; the line must be somewhere in there.
    /// </summary>
    /// <param name="source">The text of one C# file of a <c>Regression/</c> folder.</param>
    /// <returns>The line numbers of the offending tests' test attributes.</returns>
    public static IReadOnlyList<int> TestsWithoutDamage(string source)
    {
        string[] lines = Lines(source);
        int[] tests = [.. Enumerable.Range(0, lines.Length).Where(i => TestAttribute().IsMatch(lines[i]))];
        int[] starts = [.. tests.Select(i => LeadingBlockStart(lines, i))];

        var offenders = new List<int>();
        for (int t = 0; t < tests.Length; t++)
        {
            int end = t + 1 < tests.Length ? starts[t + 1] : lines.Length;
            bool named = lines[starts[t]..end].Any(l => l.TrimStart().StartsWith(DamageMarker, StringComparison.Ordinal));
            if (!named)
                offenders.Add(tests[t] + 1);
        }

        return offenders;
    }

    /// <summary>
    /// <c>[InlineData]</c> rows with no comment saying which case they are, on the row itself or on the line above.
    /// </summary>
    /// <param name="source">The text of one C# file.</param>
    /// <returns>The offending line numbers.</returns>
    public static IReadOnlyList<int> TheoryRowsWithoutComment(string source)
    {
        string[] lines = Lines(source);
        return [.. Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].TrimStart().StartsWith("[InlineData", StringComparison.Ordinal))
            .Where(i => !lines[i].Contains("//", StringComparison.Ordinal)
                && !(i > 0 && lines[i - 1].TrimStart().StartsWith("//", StringComparison.Ordinal)))
            .Select(i => i + 1)];
    }

    /// <summary>Whether a file declares tests at all.</summary>
    /// <param name="source">The text of one C# file.</param>
    /// <returns><see langword="true"/> when a line holds a test attribute.</returns>
    public static bool DeclaresTests(string source) => Lines(source).Any(l => TestAttribute().IsMatch(l));

    private static int LeadingBlockStart(string[] lines, int attribute)
    {
        int start = attribute;
        while (start > 0 && lines[start - 1].TrimStart() is var above && (above.StartsWith("//", StringComparison.Ordinal) || above.StartsWith('[')))
            start--;
        return start;
    }

    private static string[] Lines(string source) => source.Split('\n');

    // The code part of a line: a sleep named in a comment is a sentence, not a wait.
    private static string CodeOf(string line)
    {
        int comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    [GeneratedRegex(@"\b(Thread\s*\.\s*Sleep|Task\s*\.\s*Delay)\s*\(")]
    private static partial Regex WallClockWait();

    [GeneratedRegex(@"^\s*\[(Fact|Theory|ReleaseFact)\b")]
    private static partial Regex TestAttribute();
}
