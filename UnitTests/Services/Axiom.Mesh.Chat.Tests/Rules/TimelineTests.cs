using System.Linq;
using Axiom.Mesh.Chat.Tests.Support;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// SPEC.md §9.3 and §9.4 as a timeline applies them: messages line up by clock, then device id, then hash, identically
/// whatever order they arrived in; a withdrawn or broken message keeps its place and shows nothing.
/// </summary>
public sealed class TimelineTests
{
    // §9.3: by clock first, then the device id, then the hash, as unsigned byte strings; newest first reverses it exactly.
    [Fact]
    public void MessagesLineUpByClockThenDeviceThenHash_WhateverOrderTheyArrivedIn()
    {
        EntryKey[] arrived =
        [
            Facts.Key(2, Facts.Laptop, 0x10),
            Facts.Key(1, Facts.Desktop, 0x05),
            Facts.Key(1, Facts.Laptop, 0x99),
            Facts.Key(1, Facts.Laptop, 0x02),
            Facts.Key(0, Facts.Desktop, 0xff),
        ];

        EntryKey[] ordered = [.. Timeline.Order(arrived, k => k, reverse: false)];
        EntryKey[] newestFirst = [.. Timeline.Order(arrived.Reverse(), k => k, reverse: true)];

        Assert.Equal([arrived[4], arrived[3], arrived[2], arrived[1], arrived[0]], ordered);
        Assert.Equal(ordered.Reverse(), newestFirst);
        Assert.True(Timeline.Later(arrived[0], arrived[2], reverse: false));
        Assert.True(Timeline.Later(arrived[2], arrived[0], reverse: true));
        Assert.True(Timeline.Later(arrived[2], null, reverse: true));
        Assert.False(Timeline.Later(arrived[2], arrived[2], reverse: false));
    }

    // §9.4, PAYLOADS.md §1.2: only a message neither withdrawn nor broken shows its content; a preview cuts between
    // characters, never inside one.
    [Fact]
    public void OnlyAMessageNeitherWithdrawnNorBrokenShowsItsContent_APreviewCutsBetweenCharacters()
    {
        Assert.True(Timeline.Visible(withdrawn: false, broken: false));
        Assert.False(Timeline.Visible(withdrawn: true, broken: false));
        Assert.False(Timeline.Visible(withdrawn: false, broken: true));
        Assert.Equal("hello", Timeline.Preview("hello", 5));
        Assert.Equal("hell", Timeline.Preview("hello", 4));
        Assert.Equal("ab", Timeline.Preview("abé", 3));
        Assert.Equal("a", Timeline.Preview("a\U0001F600", 4));
        Assert.Equal("a\U0001F600", Timeline.Preview("a\U0001F600", 5));
    }
}
