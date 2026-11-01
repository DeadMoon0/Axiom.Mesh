using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Chat;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh chat show &lt;channel&gt; [--limit &lt;n&gt;] [--reverse] [--after &lt;entry&gt;] [--json]</c>: a Channel's
/// messages in the total order every device agrees on (SPEC.md §9.3, CHAT-SERVICE.md §5.3), oldest first; a withdrawn
/// message never shows its text.
/// </summary>
/// <param name="Channel">The Channel as typed: id, prefix or name.</param>
/// <param name="Limit">How many messages at most.</param>
/// <param name="Reverse">Newest first.</param>
/// <param name="After">The message to start after, as typed; <see langword="null"/> for the start.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record ChatShowCommand(string Channel, int Limit, bool Reverse, string? After, bool Json) : Command
{
    /// <summary>The default number of messages shown.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The most messages one command shows.</summary>
    public const int MaxLimit = 10_000;

    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        PackageId package = await IdResolver.PackageAsync(context, Channel).ConfigureAwait(false);
        EntryKey? after = null;
        if (After is not null)
        {
            ObjectHash start = await IdResolver.EntryAsync(context, After).ConfigureAwait(false);
            after = (await context.ChatAsync(new MessageRequest { Hash = start }).ConfigureAwait(false)).Message.Key;
        }

        var messages = new List<MessageView>();
        while (messages.Count < Limit)
        {
            var request = new TimelineRequest { PackageId = package, After = after, Reverse = Reverse, Limit = (ushort)Math.Min(ChatLimits.TimelinePage, Limit - messages.Count) };
            TimelineReply page = await context.ChatAsync(request).ConfigureAwait(false);
            messages.AddRange(page.Messages);
            if (!page.More || page.Messages.Count == 0)
                break;
            after = page.Messages[^1].Key;
        }

        return Json ? Print(context, ChatView.TimelineJson(messages)) : Print(context, ChatView.Timeline(messages));
    }
}
