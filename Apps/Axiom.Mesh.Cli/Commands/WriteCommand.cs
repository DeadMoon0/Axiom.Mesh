using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh write &lt;package&gt; --text "…"</c>: writes a Message (PAYLOADS.md §3) to a Channel. By
/// <see cref="WritePathRule"/> (S-D5): through the chat service with <c>send</c> when it is connected and holds the Channel
/// kind, so the message is indexed and pushed at once; directly as the external service <c>cli</c> with its configured
/// token (LOCAL-SPEC.md §9.2, §9.4) when it is not, or when the call finds it gone (<c>ServiceUnavailable</c>).
/// </summary>
/// <param name="Package">The Channel as typed: id, prefix or name.</param>
/// <param name="Text">The message.</param>
internal sealed record WriteCommand(string Package, string Text) : Command
{
    /// <summary>The line a written message prints, for <c>write</c> and <c>chat send</c> alike.</summary>
    /// <param name="key">The written Entry's position.</param>
    /// <returns>The line.</returns>
    public static string Written(EntryKey key) => $"written: entry {key.Hash.Value.ToHex()} (clock {key.Clock.ToString(CultureInfo.InvariantCulture)})";

    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        PackageId package = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false);
        IReadOnlyList<Service> services = await context.SendAsync(new ListServices()).ConfigureAwait(false);
        if (WritePathRule.Decide(services, PackageKind.Channel).Service == ChatProtocol.ServiceName)
        {
            LocalResult<SendReply> sent = await context.TryChatAsync(new SendRequest { PackageId = package, Text = Text }).ConfigureAwait(false);
            if (sent.Status != Status.ServiceUnavailable)
                return Print(context, Written(context.Ok(sent).Key));
        }

        LocalResult<KindRegistration> registered = await context.TrySendAsync(await context.ServiceAsync().ConfigureAwait(false), new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message, EntryKind.Redaction] }).ConfigureAwait(false);
        if (registered.Status == Status.KindTaken)
            throw CliFailure.Problem("another service holds Channels on this Host and did not take the message; write with 'axiom-mesh chat send', or check 'axiom-mesh services'");
        context.Ok(registered);
        var message = new MessagePayload { Text = Text, Attachments = [], ReplyTo = default };
        Entry entry = await context.SendAsServiceAsync(WriteEntry.Of(package, message.ToBody())).ConfigureAwait(false);
        return Print(context, Written(entry.Key));
    }
}
