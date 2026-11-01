using System;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat.Client.Tests.Rules;

/// <summary>
/// The chat service's statuses (CHAT-SERVICE.md §7, LOCAL-SPEC.md §11.2): service-defined codes from 32 768 up, one each,
/// the zero member first; each explained in one line; a code of LOCAL-SPEC.md §11.1 is no chat status.
/// </summary>
public sealed class ChatStatusTests
{
    // §11.2: every chat status but None is 32768 or above and a status a service may reply with; the codes are the ones the
    // document lists; each has its own line; the statuses of §11.1 are not taken for chat statuses.
    [Fact]
    public void EveryChatStatusIsAServiceDefinedCodeWithItsOwnExplanation()
    {
        ChatStatus[] statuses = [.. Enum.GetValues<ChatStatus>().Where(s => s != ChatStatus.None)];

        Assert.Equal(ChatStatus.None, Enum.GetValues<ChatStatus>()[0]);
        Assert.Equal([32_768, 32_769, 32_770, 32_771, 32_772, 32_773], statuses.Select(s => (int)s));
        Assert.All(statuses, s => Assert.True(ReplyStatusRule.MayReply((Status)(ushort)s), s.ToString()));
        Assert.Equal(statuses.Length, statuses.Select(ChatStatusText.Explain).Distinct().Count());
        Assert.All(statuses, s => Assert.True(ChatStatusText.IsChatStatus((Status)(ushort)s, out ChatStatus read) && read == s));
        Assert.False(ChatStatusText.IsChatStatus(Status.NotFound, out ChatStatus none));
        Assert.Equal(ChatStatus.None, none);
        Assert.False(ChatStatusText.IsChatStatus((Status)40_000, out _));
    }
}
