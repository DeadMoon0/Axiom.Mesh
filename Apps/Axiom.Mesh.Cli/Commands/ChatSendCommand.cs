using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh chat send &lt;channel&gt; --text "…" [--file &lt;path&gt;]... [--reply &lt;entry&gt;]</c>: writes a message
/// through the chat service (CHAT-SERVICE.md §5.6). Each file or folder is attached first, as a job the command waits for
/// on the service's events (subscribed before the job starts, CHAT-SERVICE.md §8); then the message is sent with them.
/// </summary>
/// <param name="Channel">The Channel as typed: id, prefix or name.</param>
/// <param name="Text">The message's text.</param>
/// <param name="Files">The files and folders to attach, absolute or relative to the current directory.</param>
/// <param name="Reply">The message answered, as typed; <see langword="null"/> for none.</param>
internal sealed record ChatSendCommand(string Channel, string Text, IReadOnlyList<string> Files, string? Reply) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        PackageId package = await IdResolver.PackageAsync(context, Channel).ConfigureAwait(false);
        ObjectHash replyTo = Reply is null ? default : await IdResolver.EntryAsync(context, Reply).ConfigureAwait(false);
        var attachments = new List<Attachment>();
        foreach (string file in Files)
        {
            string path = Path.GetFullPath(Path.Combine(context.Environment.CurrentDirectory, file));
            JobView done = await ChatJobWait.RunAsync(context, async () => (await context.ChatAsync(new AttachRequest { Path = path }).ConfigureAwait(false)).Job).ConfigureAwait(false);
            attachments.Add(done.Attachment ?? throw CliFailure.Problem($"the chat service attached {path} but named no attachment"));
        }

        SendReply sent = await context.ChatAsync(new SendRequest { PackageId = package, Text = Text, Attachments = attachments, ReplyTo = replyTo }).ConfigureAwait(false);
        return Print(context, WriteCommand.Written(sent.Key));
    }
}
