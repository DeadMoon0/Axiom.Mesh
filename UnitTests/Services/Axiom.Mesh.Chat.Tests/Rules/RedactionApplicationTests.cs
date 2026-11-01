using Axiom.Mesh.Chat.Tests.Support;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// SPEC.md §9.4 as the index applies it, in both arrival orders: a redaction waits for a target not indexed yet, withdraws
/// it when honoured (the target's own device or an owner device, same Channel, the target no redaction), and is ignored
/// otherwise.
/// </summary>
public sealed class RedactionApplicationTests
{
    // §9.4: Pending before the target is indexed; Applied from the author's device or an owner device; Ignored from another
    // user's device, across Channels, and against a redaction.
    [Fact]
    public void ARedactionWaitsForItsTarget_IsAppliedWhenHonoured_AndIgnoredOtherwise()
    {
        EntryFacts target = new(Facts.Family, default, Facts.Key(1, Facts.Desktop, 0x20));
        EntryFacts byAuthor = new(Facts.Family, target.Key.Hash, Facts.Key(2, Facts.Desktop, 0x21));
        EntryFacts byOwner = new(Facts.Family, target.Key.Hash, Facts.Key(2, Facts.Laptop, 0x22));
        EntryFacts elsewhere = new(Facts.Other, target.Key.Hash, Facts.Key(2, Facts.Desktop, 0x23));
        EntryFacts againstARedaction = new(Facts.Family, byAuthor.Key.Hash, Facts.Key(3, Facts.Laptop, 0x24));

        Assert.Equal(RedactionOutcome.Pending, RedactionApplication.Decide(byAuthor, null, fromOwnerDevice: false));
        Assert.Equal(RedactionOutcome.Applied, RedactionApplication.Decide(byAuthor, target, fromOwnerDevice: false));
        Assert.Equal(RedactionOutcome.Applied, RedactionApplication.Decide(byOwner, target, fromOwnerDevice: true));
        Assert.Equal(RedactionOutcome.Ignored, RedactionApplication.Decide(byOwner, target, fromOwnerDevice: false));
        Assert.Equal(RedactionOutcome.Ignored, RedactionApplication.Decide(elsewhere, target, fromOwnerDevice: true));
        Assert.Equal(RedactionOutcome.Ignored, RedactionApplication.Decide(againstARedaction, byAuthor, fromOwnerDevice: true));
    }
}
