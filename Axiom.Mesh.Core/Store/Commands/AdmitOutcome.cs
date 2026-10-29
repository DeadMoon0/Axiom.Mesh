namespace Axiom.Mesh.Core;

/// <summary>What the store writer did with a storable object.</summary>
internal enum AdmitOutcome
{
    /// <summary>Not stored: nothing here wants it (an unknown user's Roster, an unfollowed Package's Grant or Entry).</summary>
    NotWanted = 0,

    /// <summary>Stored now.</summary>
    Stored = 1,

    /// <summary>Stored before; touched.</summary>
    AlreadyStored = 2,

    /// <summary>Valid, but an object held as current supersedes it (SPEC.md §8.7–§8.9): not stored.</summary>
    Superseded = 3,

    /// <summary>A Manifest with another kind than the current one: faulty content, ignored (SPEC.md §8.7).</summary>
    KindChanged = 4,

    /// <summary>Its bytes would raise the pinned bytes above the budget (LOCAL-SPEC.md §6.5): not stored.</summary>
    StoreFull = 5,
}
