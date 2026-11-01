namespace Axiom.Mesh.Chat;

/// <summary>
/// An event of <c>axiom-mesh-chat/1</c> (CHAT-SERVICE.md §6), as <see cref="ChatEventReader"/> reads it from a
/// <c>ServiceEvent</c>: <see cref="MessageEvent"/>, <see cref="WithdrawnEvent"/>, <see cref="ChannelEvent"/> or
/// <see cref="JobEvent"/>. A closed set: no type outside this library derives from it.
/// </summary>
public abstract record ChatEvent
{
    private protected ChatEvent()
    {
    }

    /// <summary>The event's name, as <see cref="ChatEvents"/> lists it.</summary>
    public abstract string Name { get; }
}
