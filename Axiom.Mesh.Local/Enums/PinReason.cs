namespace Axiom.Mesh.Local;

/// <summary>Why an object is pinned (LOCAL-SPEC.md §5.11, §14.8).</summary>
/// <remarks>Values 6 to 255 are reserved; a value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum PinReason : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Held by the Host: Entries, Manifests, Grants and Rosters of followed Packages; source the Package id, or the user id for a Roster.</summary>
    Head = 1,

    /// <summary>Held by the Host for the Package's retention policy; source the Package id.</summary>
    Policy = 2,

    /// <summary>Held by the user role; source all zero.</summary>
    User = 3,

    /// <summary>Held by the service role; source H(service name).</summary>
    Service = 4,

    /// <summary>Held by the Host on objects an Entry written here references, until a peer has fetched them; source the Package id.</summary>
    Outbound = 5,
}
