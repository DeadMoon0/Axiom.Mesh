namespace Axiom.Mesh.Sync;

/// <summary>
/// The sync service's namespace of the local protocol (SYNC-SERVICE.md §2): its configured name, which every
/// <c>ServiceCall</c> names and every <c>ServiceEvent</c> carries, and the namespace's name.
/// </summary>
public static class SyncProtocol
{
    /// <summary>The service's name in the Host's configuration and in <c>ServiceCall.service</c>: <c>sync</c>.</summary>
    public const string ServiceName = "sync";

    /// <summary>The namespace this library speaks: <c>axiom-mesh-sync/1</c>.</summary>
    public const string Namespace = "axiom-mesh-sync/1";

    /// <summary>The marker file the service writes into every bound directory: the binding's id and its Archive.</summary>
    public const string MarkerName = ".axiom-mesh-sync";

    /// <summary>The branch a binding follows unless it names another (PAYLOADS.md §2).</summary>
    public const string DefaultLabel = "main";
}
