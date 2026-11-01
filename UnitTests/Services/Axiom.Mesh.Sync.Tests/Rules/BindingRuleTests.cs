namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SYNC-SERVICE.md §2: a directory is kept in sync once, with all below it: a bound one, one inside a bound one and one holding a
/// bound one are refused.
/// </summary>
public sealed class BindingRuleTests
{
    [Theory]
    [InlineData("/home/a/shared", false, (int)BindingVerdict.AlreadyBound)] // the same directory
    [InlineData("/home/a/shared/", false, (int)BindingVerdict.AlreadyBound)] // a trailing separator changes nothing
    [InlineData("/home/a/shared/sub", false, (int)BindingVerdict.Nested)] // inside a bound one
    [InlineData("/home/a", false, (int)BindingVerdict.Nested)] // holding a bound one
    [InlineData("/home/a/shared-2", false, (int)BindingVerdict.Allowed)] // a sibling that only starts alike
    [InlineData("/home/a/SHARED", false, (int)BindingVerdict.Allowed)] // another directory where case matters
    [InlineData(@"C:\Users\A\Shared", true, (int)BindingVerdict.AlreadyBound)] // Windows: either separator, no case
    [InlineData(@"C:\users\a\shared\x", true, (int)BindingVerdict.Nested)] // Windows: inside, whatever the case
    public void ADirectoryIsBoundOnce_WithAllBelowIt(string candidate, bool caseInsensitive, int expected)
    {
        string[] bound = caseInsensitive ? ["C:/Users/A/Shared"] : ["/home/a/shared"];

        Assert.Equal((BindingVerdict)expected, BindingRule.Check(bound, candidate, caseInsensitive));
    }
}
