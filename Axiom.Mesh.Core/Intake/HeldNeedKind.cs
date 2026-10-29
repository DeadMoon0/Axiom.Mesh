namespace Axiom.Mesh.Core;

/// <summary>What a held object waits for (SPEC.md §10.6).</summary>
internal enum HeldNeedKind
{
    /// <summary>Nothing; not held.</summary>
    None = 0,

    /// <summary>A Roster waits for its user to be known: paired, or owner or grantee of a followed Package.</summary>
    KnownUser = 1,

    /// <summary>A Manifest waits for its Package to be followed or listed.</summary>
    FollowedOrListed = 2,

    /// <summary>A Grant or Entry waits for the current Manifest of its followed Package.</summary>
    Manifest = 3,

    /// <summary>An Entry waits for a current Roster listing its device.</summary>
    WritersRoster = 4,

    /// <summary>An Entry waits for the current Grant to its device's user.</summary>
    WritersGrant = 5,
}
