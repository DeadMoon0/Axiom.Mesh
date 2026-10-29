namespace Axiom.Mesh.Core;

/// <summary>A reason to keep an object (SPEC.md §11.2; LOCAL-SPEC.md §14.8, whose values these are).</summary>
internal enum PinReason : byte
{
    /// <summary>No reason; never stored as a pin.</summary>
    None = 0,

    /// <summary>The Manifest, Grants, Rosters and Entries of a followed Package; source the Package id, for a Roster the user id.</summary>
    Head = 1,

    /// <summary>The Package's retention policy wants it; source the Package id.</summary>
    Policy = 2,

    /// <summary>The user said keep; source all zero.</summary>
    User = 3,

    /// <summary>A local service put it or asked to keep it; source <c>H(service name)</c>.</summary>
    Service = 4,

    /// <summary>An Entry written on this device references it and no peer has fetched it yet; source the Package id.</summary>
    Outbound = 5,
}
