namespace Axiom.Mesh.Sync;

/// <summary>
/// The methods of <c>axiom-mesh-sync/1</c> (SYNC-SERVICE.md §5), as <c>ServiceCall.method</c> names them. Each has its
/// request and reply record in this library: <c>bind</c> is <see cref="BindRequest"/> and <see cref="BindReply"/>. Every
/// method replies at once from the service's state; the work a method starts runs afterwards and <c>binding</c> events
/// report it.
/// </summary>
public static class SyncMethods
{
    /// <summary>Every binding of the service.</summary>
    public const string Bindings = "bindings";

    /// <summary>Binds a directory to an Archive and queues its first run.</summary>
    public const string Bind = "bind";

    /// <summary>Ends a binding; the directory's files stay.</summary>
    public const string Unbind = "unbind";

    /// <summary>The binding a directory lies in, or every binding.</summary>
    public const string Status = "status";

    /// <summary>Queues a run of one binding, or of every binding.</summary>
    public const string Scan = "scan";

    /// <summary>A page of the conflicts the service recorded.</summary>
    public const string Conflicts = "conflicts";
}
