namespace Axiom.Mesh.Chat;

/// <summary>
/// The methods of <c>axiom-mesh-chat/1</c> (CHAT-SERVICE.md §5), as <c>ServiceCall.method</c> names them. Each has its
/// request and reply record in this library: <c>about</c> is <see cref="AboutRequest"/> and <see cref="AboutReply"/>.
/// </summary>
public static class ChatMethods
{
    /// <summary>What the service is and how far it has indexed.</summary>
    public const string About = "about";

    /// <summary>Every Channel the service knows.</summary>
    public const string Channels = "channels";

    /// <summary>A page of one Channel's messages in the total order.</summary>
    public const string Timeline = "timeline";

    /// <summary>One message.</summary>
    public const string Message = "message";

    /// <summary>A message's thread: its root and the replies under it.</summary>
    public const string Thread = "thread";

    /// <summary>Writes a message.</summary>
    public const string Send = "send";

    /// <summary>Withdraws a message.</summary>
    public const string Withdraw = "withdraw";

    /// <summary>Stores a file or a folder as an attachment, as a job.</summary>
    public const string Attach = "attach";

    /// <summary>Fetches an attachment and writes it to disk, as a job.</summary>
    public const string Get = "get";

    /// <summary>The running and the recently ended jobs.</summary>
    public const string Jobs = "jobs";
}
