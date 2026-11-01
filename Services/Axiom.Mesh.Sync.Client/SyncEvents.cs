namespace Axiom.Mesh.Sync;

/// <summary>
/// The events of <c>axiom-mesh-sync/1</c> (SYNC-SERVICE.md §6), as <c>ServiceEvent.event</c> names them. Each has its
/// record in this library (<c>binding</c> is <see cref="BindingEvent"/>); <see cref="SyncEventReader"/> reads them.
/// </summary>
public static class SyncEvents
{
    /// <summary>A binding changed state or ended a run; at most once a second while it runs, always at the end.</summary>
    public const string Binding = "binding";

    /// <summary>The service recorded a conflict.</summary>
    public const string Conflict = "conflict";
}
