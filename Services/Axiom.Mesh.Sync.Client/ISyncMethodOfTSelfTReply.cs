using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>
/// A method of <c>axiom-mesh-sync/1</c> (SYNC-SERVICE.md §5) as its request record: the method's name and, as a type
/// parameter, the reply it answers, so that <see cref="SyncCall.SendAsync"/> cannot pair a request with another method's
/// reply.
/// </summary>
/// <typeparam name="TSelf">The request record itself.</typeparam>
/// <typeparam name="TReply">The reply record of the method.</typeparam>
public interface ISyncMethod<TSelf, TReply> : IWireRecord<TSelf>
    where TSelf : class, ISyncMethod<TSelf, TReply>
    where TReply : class, IWireRecord<TReply>
{
    /// <summary>The method's name, as <see cref="SyncMethods"/> lists it.</summary>
    static abstract string Method { get; }
}
