namespace Axiom.Mesh.Chat;

/// <summary>What taking in one Entry changed in the index (<see cref="ChatIndex.IndexAsync"/>).</summary>
internal enum IndexChange
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Nothing: the Entry was indexed before, or is no message and no redaction.</summary>
    Nothing = 1,

    /// <summary>A message was indexed (it may have arrived withdrawn or broken).</summary>
    NewMessage = 2,

    /// <summary>A redaction withdrew a message indexed before.</summary>
    Withdrew = 3,

    /// <summary>A redaction was kept: its target is not indexed yet, or SPEC.md §9.4 does not honour it.</summary>
    Kept = 4,
}
