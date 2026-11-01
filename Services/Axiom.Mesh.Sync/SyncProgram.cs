using System.Reflection;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Sync;

/// <summary>
/// Who the sync service is (LOCAL-SPEC.md §4.2, §9.1, SYNC-SERVICE.md §2): its name, its version and its kinds, as the Host's
/// default configuration lists them (<c>services.sync</c>: kinds [1], entryKinds [1]).
/// </summary>
public static class SyncProgram
{
    /// <summary>The service's version: the assembly's informational version.</summary>
    public static string Version { get; } =
        typeof(SyncProgram).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(SyncProgram).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>The definition: <c>sync</c>, the Archive kind with the Version Entry kind.</summary>
    public static ServiceDefinition Definition { get; } = new()
    {
        Name = SyncProtocol.ServiceName,
        Version = Version,
        Kinds = [new ServiceKind { Kind = PackageKind.Archive, EntryKinds = [EntryKind.Version] }],
    };
}
