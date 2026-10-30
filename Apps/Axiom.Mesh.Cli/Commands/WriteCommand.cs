using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh write &lt;package&gt; --text "…"</c>: writes a Message to a Channel (SPEC.md §8.5) as the external service
/// <c>cli</c> with its configured token (LOCAL-SPEC.md §9.2, §9.4).
/// </summary>
/// <param name="Package">The Channel as typed: id, prefix or name.</param>
/// <param name="Text">The message.</param>
internal sealed record WriteCommand(string Package, string Text) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        PackageId package = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false);
        await context.SendAsServiceAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message, EntryKind.Redaction] }).ConfigureAwait(false);
        var message = new MessagePayload { Text = Text, Attachments = [], ReplyTo = default };
        Entry entry = await context.SendAsServiceAsync(new WriteEntry { PackageId = package, Kind = EntryKind.Message, Payload = PayloadEncoder.Encode(message) }).ConfigureAwait(false);
        return Print(context, $"written: entry {entry.Hash.Value.ToHex()} (clock {entry.Clock})");
    }
}
