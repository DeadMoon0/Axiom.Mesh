using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>
/// A method of <c>axiom-mesh-chat/1</c> (CHAT-SERVICE.md §5) as its request record: the method's name and, as a type
/// parameter, the reply it answers, so that <see cref="ChatCall.SendAsync"/> cannot pair a request with another
/// method's reply.
/// </summary>
/// <typeparam name="TSelf">The request record itself.</typeparam>
/// <typeparam name="TReply">The reply record of the method.</typeparam>
public interface IChatMethod<TSelf, TReply> : IWireRecord<TSelf>
    where TSelf : class, IChatMethod<TSelf, TReply>
    where TReply : class, IWireRecord<TReply>
{
    /// <summary>The method's name, as <see cref="ChatMethods"/> lists it.</summary>
    static abstract string Method { get; }
}
