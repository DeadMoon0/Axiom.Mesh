using System.Threading.Tasks;
using Axiom.Mesh.Chat;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh chat withdraw &lt;entry&gt;</c>: withdraws a message (CHAT-SERVICE.md §5.7, SPEC.md §9.4). Only the device
/// that wrote it or a device of the Channel's owner may; every device then shows it withdrawn, never its text.
/// </summary>
/// <param name="Entry">The message as typed: hash or prefix.</param>
internal sealed record ChatWithdrawCommand(string Entry) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        ObjectHash message = await IdResolver.EntryAsync(context, Entry).ConfigureAwait(false);
        WithdrawReply withdrawn = await context.ChatAsync(new WithdrawRequest { Message = message }).ConfigureAwait(false);
        return Print(context, $"withdrawn: entry {message.Value.ToHex()} by redaction {withdrawn.Key.Hash.Value.ToHex()}");
    }
}
