namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SYNC-SERVICE.md §2: a binding leaves alone exact names and extensions, matched against every name of a path, and always the
/// names starting with <c>.axiom-mesh-</c>.
/// </summary>
public sealed class IgnoreRuleTests
{
    [Theory]
    [InlineData("node_modules/x/y.js", true)] // an ignored directory, with everything in it
    [InlineData("src/app.TMP", true)] // an extension, without case
    [InlineData("src/app.tmp.keep", false)] // only the last extension counts
    [InlineData("Thumbs.db", true)] // an exact name
    [InlineData("thumbs.db.txt", false)] // an exact name matches whole
    [InlineData(".axiom-mesh-sync", true)] // the marker, always
    [InlineData("a/.axiom-mesh-partial-b", true)] // a temporary file, always
    [InlineData("readme.md", false)] // anything else is synced
    public void NamesAndExtensionsMatchAnyNameOfAPath_ReservedNamesAlways(string path, bool ignored)
    {
        Assert.True(IgnoreRule.TryParse(["node_modules", "*.tmp", "Thumbs.db"], out IgnoreRule? rule, out string? problem), problem);
        Assert.Equal(ignored, rule!.Matches(path));
    }

    [Theory]
    [InlineData("a/b")] // a path, not a name
    [InlineData("build*")] // a star only first
    [InlineData("*")] // a star needs an extension
    [InlineData("")] // nothing
    public void APatternWithAPathOrAStarElsewhereIsRefused(string pattern)
    {
        Assert.False(IgnoreRule.TryParse([pattern], out _, out string? problem));
        Assert.NotNull(problem);
    }
}
