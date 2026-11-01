namespace Axiom.Mesh.Chat;

/// <summary>
/// The chat service's namespace of the local protocol (CHAT-SERVICE.md §2): its configured name, which every
/// <c>ServiceCall</c> names and every <c>ServiceEvent</c> carries, and the namespace's name, which <c>about</c> answers.
/// </summary>
public static class ChatProtocol
{
    /// <summary>The service's name in the Host's configuration and in <c>ServiceCall.service</c>: <c>chat</c>.</summary>
    public const string ServiceName = "chat";

    /// <summary>The namespace this library speaks: <c>axiom-mesh-chat/1</c>.</summary>
    public const string Namespace = "axiom-mesh-chat/1";
}
