using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The service's own check of its kinds' payloads (PAYLOADS.md §1.2 to §4): a Message must parse exactly, name in its
/// envelope's <c>references</c> exactly its attachments' hashes (each once, ascending) and redact nothing; a Redaction must
/// parse and its <c>target</c> must equal the envelope's <c>redacts</c>. What fails is broken: placed in the timeline, never
/// shown. The Host has stored and forwarded it all the same, which is correct. Pure.
/// </summary>
internal static class PayloadCheck
{
    /// <summary>Checks a Message.</summary>
    /// <param name="kind">The Entry's kind.</param>
    /// <param name="payload">Its payload.</param>
    /// <param name="references">Its envelope's <c>references</c>, as in the object.</param>
    /// <param name="redacts">Its envelope's <c>redacts</c>.</param>
    /// <returns>The payload, when it parses, and whether the Entry is broken.</returns>
    public static MessageCheck OfMessage(EntryKind kind, ReadOnlyMemory<byte> payload, IReadOnlyList<ObjectHash> references, ObjectHash redacts)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (kind != EntryKind.Message || !EntryPayload.TryParse(kind, payload, out EntryPayload? parsed) || parsed is not MessagePayload message)
            return new MessageCheck(null, true);
        ObjectHash[] named = [.. message.Attachments.Select(a => a.Hash).Distinct().Order()];
        bool envelopeHolds = redacts.IsZero && named.SequenceEqual(references);
        return new MessageCheck(message, !envelopeHolds);
    }

    /// <summary>Whether a Redaction is broken: its payload does not parse, or names another target than its envelope.</summary>
    /// <param name="kind">The Entry's kind.</param>
    /// <param name="payload">Its payload.</param>
    /// <param name="redacts">Its envelope's <c>redacts</c>.</param>
    /// <returns><see langword="true"/> when broken.</returns>
    public static bool RedactionIsBroken(EntryKind kind, ReadOnlyMemory<byte> payload, ObjectHash redacts) =>
        kind != EntryKind.Redaction
        || !EntryPayload.TryParse(kind, payload, out EntryPayload? parsed)
        || parsed is not RedactionPayload redaction
        || redaction.Target != redacts;
}
