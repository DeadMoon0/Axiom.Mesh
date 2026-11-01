using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>
/// What each <see cref="ChatStatus"/> means for a person, in one line (CHAT-SERVICE.md §7), for an app that explains a
/// failed call. The statuses of LOCAL-SPEC.md §11.1 are the app's own to explain.
/// </summary>
public static class ChatStatusText
{
    /// <summary>Whether a call's status is one the chat service defines.</summary>
    /// <param name="status">The status the call failed with.</param>
    /// <param name="chat">The chat status, or <see cref="ChatStatus.None"/>.</param>
    /// <returns><see langword="true"/> for a code of <see cref="ChatStatus"/>.</returns>
    public static bool IsChatStatus(Status status, out ChatStatus chat)
    {
        chat = (ChatStatus)(ushort)status;
        bool known = chat is ChatStatus.NotAChannel or ChatStatus.NotAMessage or ChatStatus.NotYours or ChatStatus.AttachmentMissing or ChatStatus.Withdrawn or ChatStatus.JobNotFound;
        if (!known)
            chat = ChatStatus.None;
        return known;
    }

    /// <summary>The line for a chat status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>One line, never empty.</returns>
    public static string Explain(ChatStatus status) => status switch
    {
        ChatStatus.None => "done",
        ChatStatus.NotAChannel => "that is not a Channel the chat service knows; follow it first, or name a Channel, not an Archive",
        ChatStatus.NotAMessage => "the chat service knows no such message",
        ChatStatus.NotYours => "only the device that wrote the message, or a device of the Channel's owner, may withdraw it",
        ChatStatus.AttachmentMissing => "the attachment is not stored here; attach it again, or name one the message has",
        ChatStatus.Withdrawn => "the message is withdrawn; nothing of it is shown or fetched any more",
        ChatStatus.JobNotFound => "the chat service has no such job",
        _ => $"the chat service refused with status {(ushort)status}",
    };
}
