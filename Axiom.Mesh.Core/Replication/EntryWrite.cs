using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>
/// What an Entry written on this device says (LOCAL-SPEC.md §9.4): the Package, the payload, the parents the writer named
/// (none for the Host's choice of SPEC.md §9.3) and, for an extension kind whose payload the Host does not parse, the
/// objects the writer says it references.
/// </summary>
/// <param name="Package">The Package.</param>
/// <param name="Payload">The payload; its kind is the Entry's kind.</param>
/// <param name="Parents">The parents named; empty for the Host's choice.</param>
/// <param name="References">The objects an extension kind's payload references; empty for kinds 1 to 3, whose references come from the parse.</param>
internal sealed record EntryWrite(PackageId Package, EntryPayload Payload, EntryParents Parents, IReadOnlyList<ObjectHash> References)
{
    /// <summary>The objects the Entry references: from the payload for kinds 1 to 3, as stated for any other.</summary>
    public IReadOnlyList<ObjectHash> Referenced => Payload is OpaquePayload ? References : Payload.References;
}
