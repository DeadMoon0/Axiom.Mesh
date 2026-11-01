using Axiom.Mesh.Chat.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// <c>withdraw</c> (CHAT-SERVICE.md §5.7, SPEC.md §9.4): the device that wrote a message, or any device of the Channel's
/// owner, may withdraw it, once; anyone else gets NotYours before a Redaction the Host would store without honouring it.
/// </summary>
public sealed class WithdrawRuleTests
{
    // §9.4: the author's own device and every owner device pass; another device of another user is NotYours; a message not
    // indexed is NotAMessage; one withdrawn already is Withdrawn.
    [Theory]
    [InlineData(true, false, "desktop", "bob", "desktop", "bob", 0)] // Bob withdraws his own message
    [InlineData(true, false, "desktop", "alice", "laptop", "alice", 0)] // the owner withdraws Bob's
    [InlineData(true, false, "laptop", "alice", "desktop", "bob", 32_770)] // Bob may not withdraw the owner's: NotYours
    [InlineData(true, false, "phone", "alice", "desktop", "bob", 32_770)] // nor another device's message of the owner
    [InlineData(false, false, "desktop", "alice", "desktop", "bob", 32_769)] // not indexed: NotAMessage
    [InlineData(true, true, "desktop", "alice", "desktop", "bob", 32_772)] // withdrawn already: Withdrawn
    public void TheAuthorsDeviceOrAnOwnerDeviceMayWithdraw_AnyoneElseIsNotYours(bool indexed, bool withdrawn, string author, string owner, string ownDevice, string ownUser, int status)
    {
        Verdict verdict = WithdrawRule.Check(indexed, withdrawn, DeviceOf(author), UserOf(owner), DeviceOf(ownDevice), UserOf(ownUser));

        Assert.Equal((Status)(ushort)status, verdict.Status);
    }

    private static DeviceId DeviceOf(string name) => name switch
    {
        "laptop" => Facts.Laptop,
        "phone" => Facts.Phone,
        _ => Facts.Desktop,
    };

    private static UserId UserOf(string name) => name == "alice" ? Facts.Alice : Facts.Bob;
}
