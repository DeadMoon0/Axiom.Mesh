using System;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §9.8, §11.1, §11.2 and §3.6, what a service may answer and until when: <c>Ok</c>, the codes of §11.1 but
/// <c>Cancelled</c>, and the service-defined codes from 32 768; nothing more than 120 seconds after the callback arrived.
/// </summary>
public sealed class ReplyRuleTests
{
    [Theory]
    [InlineData((ushort)0, true)] // Ok
    [InlineData((ushort)1, true)] // UnknownRequest, the lowest code of §11.1
    [InlineData((ushort)15, true)] // Draining
    [InlineData((ushort)16, false)] // Cancelled is never the status of an answer
    [InlineData((ushort)17, true)] // IoError, what a failed handler answers
    [InlineData((ushort)20, true)] // StoreFull, the highest code of §11.1
    [InlineData((ushort)21, false)] // the first reserved code
    [InlineData((ushort)32_767, false)] // the last reserved code
    [InlineData((ushort)32_768, true)] // the first service-defined code (§11.2)
    [InlineData((ushort)65_535, true)] // the last one
    public void AReplyCarriesOkACodeOfSection11Point1ButCancelledOrAServiceDefinedCode(ushort code, bool allowed)
    {
        Assert.Equal(allowed, ReplyStatusRule.MayReply((Status)code));
    }

    [Theory]
    [InlineData(0, true)] // at once
    [InlineData(120, true)] // the last moment
    [InlineData(121, false)] // too late: the Host may have forgotten the id after its 5 minutes, so never risk it
    public void AReplyLeavesAtMost120SecondsAfterItsCallbackArrived(int seconds, bool sent)
    {
        Assert.Equal(sent, CallbackReplyRule.MaySend(TimeSpan.FromSeconds(seconds)));
    }

    // §9.8, §5.14: a failure needs a status a reply may carry and an Error within its bounds; Ok carries a payload only.
    [Fact]
    public void AFailureNeedsAnAllowedStatusAndABoundedError()
    {
        Assert.Throws<ArgumentException>(() => CallReply.Failure(Status.Ok, new Error()));
        Assert.Throws<ArgumentException>(() => CallReply.Failure(Status.Cancelled, new Error()));
        Assert.Throws<ArgumentException>(() => CallReply.Failure((Status)21, new Error()));
        Assert.Throws<ArgumentException>(() => CallReply.Failure(Status.Invalid, new Error { Message = new string('x', 257) }));
        Assert.Throws<ArgumentException>(() => CallReply.Ok(new byte[Limits.MaxPayloadLength + 1]));
        Assert.Equal((Status)32_768, CallReply.Failure((Status)32_768, new Error { Message = "not a channel" }).Status);
        Assert.True(CallReply.Ok(new byte[] { 1 }).IsOk);
    }
}
