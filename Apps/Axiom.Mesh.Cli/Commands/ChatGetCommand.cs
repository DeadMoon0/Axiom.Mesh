using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh chat get &lt;entry&gt; &lt;attachment&gt; --out &lt;path&gt; [--existing fail|replace|merge]</c>: fetches
/// one attachment of a message from the peers and writes it, a file or a folder (CHAT-SERVICE.md §5.9), as a job the
/// command waits for.
/// </summary>
/// <param name="Entry">The message as typed: hash or prefix.</param>
/// <param name="Attachment">The attachment: its exact name, or a prefix of its hash.</param>
/// <param name="Out">Where to write it, absolute or relative to the current directory.</param>
/// <param name="Existing">What to do when the path exists.</param>
internal sealed record ChatGetCommand(string Entry, string Attachment, string Out, ExistingPath Existing) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        ObjectHash hash = await IdResolver.EntryAsync(context, Entry).ConfigureAwait(false);
        MessageView message = (await context.ChatAsync(new MessageRequest { Hash = hash }).ConfigureAwait(false)).Message;
        if (message.Withdrawn)
            throw CliFailure.Problem(ChatStatusText.Explain(ChatStatus.Withdrawn));
        Attachment named = Pick(message);
        string path = Path.GetFullPath(Path.Combine(context.Environment.CurrentDirectory, Out));
        var request = new GetRequest { Hash = named.Hash, Message = hash, Path = path, Existing = Existing };
        JobView done = await ChatJobWait.RunAsync(context, async () => (await context.ChatAsync(request).ConfigureAwait(false)).Job).ConfigureAwait(false);
        return Print(context, ChatView.JobDone(done));
    }

    // The attachment by its exact name, else by a prefix of its hash that names one.
    private Attachment Pick(MessageView message)
    {
        Attachment[] byName = [.. message.Attachments.Where(a => a.Name == Attachment)];
        if (byName.Length == 1)
            return byName[0];
        Attachment[] byHash = [.. message.Attachments.Where(a => Attachment.Length >= 4 && a.Hash.Value.ToHex().StartsWith(Attachment, StringComparison.OrdinalIgnoreCase))];
        return byHash.Length == 1
            ? byHash[0]
            : throw CliFailure.Usage(byName.Length + byHash.Length == 0
                ? $"the message has no attachment named or hashed '{Attachment}'; it has {(message.Attachments.Count == 0 ? "none" : string.Join(", ", message.Attachments.Select(a => a.Name)))}"
                : $"'{Attachment}' names more than one attachment; give more of its hash");
    }
}
