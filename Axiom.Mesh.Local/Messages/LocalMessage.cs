using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// One frame of <c>axiom-mesh-local/1</c> (LOCAL-SPEC.md §3): a session message, or the envelope of a request, a
/// response, an event, a callback or a reply. What a client may send is an <see cref="IClientMessage"/>, what the Host
/// may send an <see cref="IHostMessage"/>; <see cref="Bye"/>, <see cref="Ping"/> and <see cref="Pong"/> are both.
/// </summary>
/// <remarks>The set is closed: every message is a sealed class of this package.</remarks>
public abstract class LocalMessage
{
    private protected LocalMessage()
    {
    }

    /// <summary>The frame's message id (§14.1): the session message's own, 32 for a response, 33 for a reply, or the request's, event's or callback's.</summary>
    public abstract ushort MessageId { get; }

    /// <summary>Writes the frame's payload, everything after the message id.</summary>
    /// <param name="writer">The writer.</param>
    internal abstract void WritePayload(ref WireWriter writer);
}
