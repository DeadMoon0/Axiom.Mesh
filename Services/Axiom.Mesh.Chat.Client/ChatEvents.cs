namespace Axiom.Mesh.Chat;

/// <summary>
/// The events of <c>axiom-mesh-chat/1</c> (CHAT-SERVICE.md §6), as <c>ServiceEvent.event</c> names them. Each has its
/// record in this library (<c>message</c> is <see cref="MessageEvent"/>); <see cref="ChatEventReader"/> reads them.
/// </summary>
public static class ChatEvents
{
    /// <summary>A new message was indexed; never sent for a withdrawn one.</summary>
    public const string Message = "message";

    /// <summary>A message was withdrawn.</summary>
    public const string Withdrawn = "withdrawn";

    /// <summary>A Channel was indexed, followed, unfollowed, dropped or renamed.</summary>
    public const string Channel = "channel";

    /// <summary>A job started, progressed or ended.</summary>
    public const string Job = "job";
}
