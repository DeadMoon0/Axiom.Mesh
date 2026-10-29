namespace Axiom.Mesh;

/// <summary>Why a node treats a Roster's device as removed (SPEC.md §8.8), or that it does not.</summary>
public enum DeviceRemoval
{
    /// <summary>Not removed: the device may connect and write.</summary>
    None = 0,

    /// <summary>The Roster marks it removed.</summary>
    Flagged = 1,

    /// <summary>An earlier verified Roster of the user marked it removed or omitted it; removal is permanent.</summary>
    RemovedBefore = 2,

    /// <summary>The current Roster of another user, verified earlier, lists the same device id.</summary>
    HeldByAnotherUser = 3,
}
