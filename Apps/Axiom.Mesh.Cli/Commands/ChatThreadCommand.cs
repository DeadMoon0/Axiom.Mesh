using System.Threading.Tasks;
using Axiom.Mesh.Chat;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh chat thread &lt;entry&gt; [--json]</c>: the thread a message belongs to, its root and every reply under it
/// in the total order (CHAT-SERVICE.md §5.5); a withdrawn root keeps its replies.
/// </summary>
/// <param name="Entry">Any message of the thread, as typed: hash or prefix.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record ChatThreadCommand(string Entry, bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        ObjectHash message = await IdResolver.EntryAsync(context, Entry).ConfigureAwait(false);
        ThreadReply thread = await context.ChatAsync(new ThreadRequest { Message = message, Limit = ChatLimits.TimelinePage }).ConfigureAwait(false);
        return Json ? Print(context, ChatView.ThreadJson(thread)) : Print(context, ChatView.Thread(thread));
    }
}
