using Axiom.Mesh.ReadmeSamples.Tests.Support;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.ReadmeSamples.Tests.Regression;

/// <summary>
/// The source scanners behind the convention tests still find what they are meant to find. On a clean repository every
/// convention passes, and a scanner that stopped matching would pass with it; these samples make it fail instead.
/// Samples are built from single-line strings so that this file's own lines never look like what they describe.
/// </summary>
public sealed class ConventionScannerTests
{
    private static string Source(params string[] lines) => string.Join("\n", lines);

    // Damage: a wall-clock wait slips into a suite and makes it slow and flaky, with the convention still green.
    [Fact]
    public void AWaitByTheWallClockIsFound_ButNotWhenAnotherWordOrACommentNamesIt()
    {
        string source = Source(
            "await Task" + ".Delay(10);",
            "Thread" + ".Sleep(1);",
            "// Thread" + ".Sleep(1) would be wrong here",
            "await clock.Delay(10);");

        Assert.Equal([1, 2], TestSourceRules.SleepsAndDelays(source));
    }

    // Damage: a regression test without its damage line, or one that borrows the next test's line, passes the convention.
    [Fact]
    public void ARegressionTestWithoutItsOwnDamageLineIsFound()
    {
        string source = Source(
            "    // Damage: the first one pins something.",
            "    [Fact]",
            "    public void First() { }",
            string.Empty,
            "    [Fact]",
            "    public void Second() { }",
            string.Empty,
            "    // a comment, but no damage",
            "    [Theory]",
            "    public void Third(int x) { }");

        Assert.Equal([5, 9], TestSourceRules.TestsWithoutDamage(source));
    }

    // Damage: a theory row nobody can tell apart from the next passes the convention.
    [Fact]
    public void ATheoryRowWithoutACommentIsFound_OnTheRowOrTheLineAboveCounts()
    {
        string source = Source(
            "    [InlineData(1)] // one",
            "    // two",
            "    [InlineData(2)]",
            "    [InlineData(3)]");

        Assert.Equal([4], TestSourceRules.TheoryRowsWithoutComment(source));
    }

    // Damage: a second type hidden in a file, or a file renamed away from its type, passes the convention.
    [Fact]
    public void AFileWithTwoTypesOrAMisnamedTypeIsFound_AGenericInNameOfTIsNot()
    {
        Assert.Null(SourceFileShape.Problem("Foo", "namespace A;\npublic sealed class Foo { private sealed class Inner { } }"));
        Assert.Null(SourceFileShape.Problem("FooOfT", "namespace A { public interface Foo<T> { } }"));
        Assert.NotNull(SourceFileShape.Problem("Foo", "namespace A;\npublic sealed class Foo { }\npublic enum Bar { None = 0 }"));
        Assert.NotNull(SourceFileShape.Problem("Foo", "namespace A;\npublic sealed class Bar { }"));
        Assert.NotNull(SourceFileShape.Problem("Foo", "namespace A;"));
    }
}
