namespace Axiom.Mesh.Local;

/// <summary>What an operation does (LOCAL-SPEC.md §8.6, §14.6).</summary>
/// <remarks>Values 1 and 3 are reserved (<c>Materialise</c> and <c>PutFile</c> until draft 3, removed in draft 4), 4 to 255 for extensions; a value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum OperationKind : ushort
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Fetches objects from peers (<c>Fetch</c>).</summary>
    Fetch = 2,
}
