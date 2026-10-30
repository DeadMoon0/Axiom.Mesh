using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// Ids on the command line (fundamentals plan §2.7): the full hex, or a prefix that names exactly one candidate; a name
/// for what has one; an ambiguous prefix is one line naming the candidates.
/// </summary>
public sealed class IdMatchTests
{
    private static readonly IReadOnlyList<(Hash32 Id, string Name)> Packages =
    [
        (Id("3f2a00"), "Photos"),
        (Id("3f2b00"), "Chat"),
        (Id("a10000"), "Notes"),
    ];

    [Theory]
    [InlineData("a", "Notes")] // one hex digit is enough when it names one
    [InlineData("3F2A", "Photos")] // upper case hex
    [InlineData("3f2b", "Chat")] // a longer prefix that separates two
    [InlineData("Chat", "Chat")] // the exact name
    public void APrefixOrANameThatNamesOneCandidateFindsIt(string text, string expected)
    {
        Assert.True(IdMatch.TryFind(text, Packages, p => p.Id, p => p.Name, "package", out var match, out string? problem, out bool ambiguous), problem);
        Assert.Equal(expected, match.Name);
        Assert.False(ambiguous);
    }

    [Fact]
    public void AnAmbiguousPrefixNamesTheCandidates_AnUnknownOneSaysNoneMatches()
    {
        Assert.False(IdMatch.TryFind("3f2", Packages, p => p.Id, p => p.Name, "package", out _, out string? problem, out bool ambiguous));
        Assert.True(ambiguous);
        Assert.Equal("'3f2' matches 2 packages: 3f2a00000000 (Photos), 3f2b00000000 (Chat); give more of the id", problem);

        Assert.False(IdMatch.TryFind("ffff", Packages, p => p.Id, p => p.Name, "package", out _, out problem, out ambiguous));
        Assert.False(ambiguous);
        Assert.Equal("no package matches 'ffff'", problem);

        Assert.False(IdMatch.TryFind("chat", Packages, p => p.Id, p => p.Name, "package", out _, out _, out _));
    }

    [Fact]
    public void AFullIdIsTakenAsItIsWithoutLookingItUp()
    {
        string full = new string('c', 64);

        Assert.True(IdMatch.IsFull(full, out Hash32 id));
        Assert.Equal(full, id.ToHex());
        Assert.False(IdMatch.IsFull(full[..63], out _));
        Assert.False(IdMatch.IsFull(new string('g', 64), out _));
    }

    private static Hash32 Id(string prefix) => Hash32.TryParseHex(prefix.PadRight(64, '0'), out Hash32 id) ? id : throw new ArgumentException(prefix);
}
