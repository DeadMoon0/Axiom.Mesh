using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// A message of the pairing protocol <c>axiom-mesh-pair/1</c> (PAIR-SPEC.md §5): <see cref="PairHello"/>,
/// <see cref="PairConfirm"/> or <see cref="PairCancel"/>. A closed set; every message owns its values.
/// </summary>
public abstract class PairMessage
{
    private protected PairMessage()
    {
    }

    /// <summary>Writes the message body, without the frame header and the id.</summary>
    /// <param name="writer">The writer.</param>
    public abstract void Write(ref WireWriter writer);
}
