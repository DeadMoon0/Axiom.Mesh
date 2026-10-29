namespace Axiom.Mesh;

/// <summary>What an Entry waits for before it can be decided (SPEC.md §10.6): it is held, not stored, until it arrives.</summary>
public enum Prerequisite
{
    /// <summary>The current Manifest of the Entry's Package.</summary>
    Manifest = 0,

    /// <summary>A current Roster that lists the Entry's device.</summary>
    WritersRoster = 1,

    /// <summary>The current Grant of the Package to the user whose Roster lists the device.</summary>
    WritersGrant = 2,
}
