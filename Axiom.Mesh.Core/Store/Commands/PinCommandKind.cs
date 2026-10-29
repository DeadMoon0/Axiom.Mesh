namespace Axiom.Mesh.Core;

/// <summary>What a <see cref="PinCommand"/> does (LOCAL-SPEC.md §6.5, SPEC.md §11.2).</summary>
internal enum PinCommandKind
{
    /// <summary>Pins each stored object with the reason and source; idempotent.</summary>
    Pin = 0,

    /// <summary>Removes each object's pin of the reason and source; never fails for a pin that is not there.</summary>
    Unpin = 1,

    /// <summary>Removes every pin of the reason and source, whatever object it holds.</summary>
    Release = 2,

    /// <summary>The objects were sent to a peer in answer to a <c>Want</c>: their <c>Outbound</c> pins go, whatever the source.</summary>
    Sent = 3,
}
