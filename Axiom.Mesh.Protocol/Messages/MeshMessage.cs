using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// A message of AMP/1 (SPEC.md §7): one of the sixteen messages of §14.1, or an <see cref="Extension"/> this node does
/// not know. A closed set: no type outside this package can derive from it.
/// </summary>
/// <remarks>
/// Every message but <see cref="ObjectMessage"/> owns its values and stays valid for ever. A received
/// <see cref="ObjectMessage"/> is a view of its frame, valid until the next read on the session.
/// </remarks>
public abstract class MeshMessage
{
    private protected MeshMessage()
    {
    }

    /// <summary>The message id of SPEC.md §14.1 that the frame header carries (§5.1).</summary>
    public abstract ushort Id { get; }

    /// <summary>Writes the message body, without the frame header and the id.</summary>
    /// <param name="writer">The writer.</param>
    public abstract void Write(ref WireWriter writer);
}
