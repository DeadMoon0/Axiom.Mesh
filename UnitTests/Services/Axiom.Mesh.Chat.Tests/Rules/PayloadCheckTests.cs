using System.Linq;
using Axiom.Mesh.Chat.Tests.Support;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// PAYLOADS.md §1.2 to §4 as the service checks its kinds: a Message must parse and name in <c>references</c> exactly its
/// attachments' hashes and redact nothing; a Redaction must parse and name in <c>target</c> what its envelope redacts. Anything
/// else is broken: placed, never shown.
/// </summary>
public sealed class PayloadCheckTests
{
    // §1.2, §1.3, §3, §4: what holds is not broken; a payload that does not parse, references that miss or add an object, a
    // Message that redacts, and a Redaction whose target differs from redacts are broken.
    [Fact]
    public void AnEntryWhoseEnvelopeDoesNotNameWhatItsPayloadNamesIsBroken_AsIsOneThatDoesNotParse()
    {
        var message = new MessagePayload { Text = "hi", Attachments = [Facts.Attachment("b", 9, 1), Facts.Attachment("a", 3, 1), Facts.Attachment("again", 9, 1)], ReplyTo = default };
        EntryBody body = message.ToBody();
        ObjectHash[] references = [.. body.References];

        MessageCheck holds = PayloadCheck.OfMessage(EntryKind.Message, body.Payload, references, default);
        MessageCheck missing = PayloadCheck.OfMessage(EntryKind.Message, body.Payload, references[..1], default);
        MessageCheck redacting = PayloadCheck.OfMessage(EntryKind.Message, body.Payload, references, Facts.Hash(0x40));
        MessageCheck garbage = PayloadCheck.OfMessage(EntryKind.Message, new byte[] { 0, 9, 1 }, [], default);

        Assert.Equal((false, "hi"), (holds.Broken, holds.Payload!.Text));
        Assert.Equal([Facts.Hash(3), Facts.Hash(9)], references);
        Assert.True(missing.Broken && missing.Payload is not null);
        Assert.True(redacting.Broken);
        Assert.True(garbage.Broken && garbage.Payload is null);

        byte[] redaction = PayloadEncoder.Encode(new RedactionPayload { Target = Facts.Hash(0x50) });
        Assert.False(PayloadCheck.RedactionIsBroken(EntryKind.Redaction, redaction, Facts.Hash(0x50)));
        Assert.True(PayloadCheck.RedactionIsBroken(EntryKind.Redaction, redaction, Facts.Hash(0x51)));
        Assert.True(PayloadCheck.RedactionIsBroken(EntryKind.Redaction, redaction.Take(10).ToArray(), Facts.Hash(0x50)));
    }
}
