namespace Axiom.Mesh.Sync;

/// <summary>
/// An event of <c>axiom-mesh-sync/1</c> (SYNC-SERVICE.md §6), as <see cref="SyncEventReader"/> reads it from a
/// <c>ServiceEvent</c>: <see cref="BindingEvent"/> or <see cref="ConflictEvent"/>. A closed set: no type outside this
/// library derives from it.
/// </summary>
public abstract record SyncEvent
{
    private protected SyncEvent()
    {
    }

    /// <summary>The event's name, as <see cref="SyncEvents"/> lists it.</summary>
    public abstract string Name { get; }
}
