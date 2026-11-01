using System;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Chat;

/// <summary>
/// Whether a <c>send</c> may be written (CHAT-SERVICE.md §5.6, PAYLOADS.md §3): the Channel is one the service knows and
/// follows; the message says something (text or an attachment); every attachment has a name; a reply answers a message of
/// the same Channel that is indexed. The bounds of the layout are the decoder's. Pure.
/// </summary>
internal static class SendRule
{
    /// <summary>Decides.</summary>
    /// <param name="request">The request.</param>
    /// <param name="channelFollowed">The Channel is indexed and followed here.</param>
    /// <param name="replyToInChannel">The message <see cref="SendRequest.ReplyTo"/> names is indexed in the same Channel; ignored for zero.</param>
    /// <returns>Pass, or why not.</returns>
    public static Verdict Check(SendRequest request, bool channelFollowed, bool replyToInChannel)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!channelFollowed)
            return Verdict.Refuse(ChatStatus.NotAChannel, $"Package {request.PackageId.Value.ToHex()[..12]} is not a Channel followed here.", "packageId");
        if (request.Text.Length == 0 && request.Attachments.Count == 0)
            return Verdict.Refuse(Status.Invalid, "A message needs a text or an attachment.", "text");
        foreach (Attachment attachment in request.Attachments)
        {
            if (string.IsNullOrEmpty(attachment.Name) || attachment.Hash.IsZero)
                return Verdict.Refuse(Status.Invalid, "Every attachment needs a name and the hash an attach job reported.", "attachments");
        }

        if (!request.ReplyTo.IsZero && !replyToInChannel)
            return Verdict.Refuse(ChatStatus.NotAMessage, "The message replied to is not indexed in this Channel.", "replyTo");
        return Verdict.Pass;
    }
}
