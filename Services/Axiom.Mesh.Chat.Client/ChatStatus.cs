namespace Axiom.Mesh.Chat;

/// <summary>
/// The statuses the chat service defines (CHAT-SERVICE.md §7, LOCAL-SPEC.md §11.2): a <c>ServiceCall</c> to <c>chat</c>
/// answers one of these as its status, with the service's <c>Error</c>. The codes of LOCAL-SPEC.md §11.1 keep their
/// meaning there.
/// </summary>
public enum ChatStatus : ushort
{
    /// <summary>No chat status: the call succeeded, or failed with a status of LOCAL-SPEC.md §11.1.</summary>
    None = 0,

    /// <summary>The Package is not a Channel the service knows (not followed here, or of another kind).</summary>
    NotAChannel = 32_768,

    /// <summary>The hash names no message the service has indexed.</summary>
    NotAMessage = 32_769,

    /// <summary>Only the message's own device or a device of the Channel's owner may withdraw it (SPEC.md §9.4).</summary>
    NotYours = 32_770,

    /// <summary>An attachment is not stored here: attach it first, or the message names no such attachment.</summary>
    AttachmentMissing = 32_771,

    /// <summary>The message is withdrawn; nothing of it is shown or fetched any more.</summary>
    Withdrawn = 32_772,

    /// <summary>No job has that number; reserved for a method that names a job.</summary>
    JobNotFound = 32_773,
}
