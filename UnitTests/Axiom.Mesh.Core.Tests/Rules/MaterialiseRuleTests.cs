namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// SPEC.md §8.4 and §11.4: a name is written only when the file system can hold it as one file name inside the
/// directory; anything else is a conflict, never a write somewhere else.
/// </summary>
public sealed class MaterialiseRuleTests
{
    [Theory]
    [InlineData("report.txt", false, true)] // an ordinary name, on Linux
    [InlineData("report.txt", true, true)] // and on Windows
    [InlineData("a:b", false, true)] // a colon is an ordinary character on Linux
    [InlineData("a:b", true, false)] // on Windows it would name an alternate data stream
    [InlineData("trailing.", true, false)] // Windows drops a trailing dot, so the name would not be the Tree's
    [InlineData("trailing ", true, false)] // or a trailing space
    [InlineData("con.txt", true, false)] // a reserved device name, whatever its extension
    [InlineData("console.txt", true, true)] // a name that only starts like one is fine
    [InlineData("back\\slash", true, false)] // a separator on Windows
    [InlineData("..", false, false)] // never the parent
    [InlineData(".axiom-mesh-materialised", false, false)] // the Host's own marker
    [InlineData(".axiom-mesh-partial-x", false, false)] // the Host's own temporary name
    public void ANameIsWrittenOnlyWhenTheFileSystemHoldsItAsOneNameInTheDirectory(string name, bool windows, bool written)
    {
        Assert.Equal(written, MaterialiseRule.MayWrite(name, windows));
    }
}
