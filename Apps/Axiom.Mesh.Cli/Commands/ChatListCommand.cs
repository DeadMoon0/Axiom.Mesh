using System.Threading.Tasks;
using Axiom.Mesh.Chat;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh chat list [--json]</c>: the Channels the chat service knows (CHAT-SERVICE.md §5.2).</summary>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record ChatListCommand(bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        ChannelsReply channels = await context.ChatAsync(new ChannelsRequest()).ConfigureAwait(false);
        return Json ? Print(context, ChatView.ChannelsJson(channels.Channels)) : Print(context, ChatView.Channels(channels.Channels));
    }
}
