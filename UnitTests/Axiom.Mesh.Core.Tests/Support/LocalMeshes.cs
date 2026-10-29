using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>
/// The meshes the local port's tests start: <c>alice-laptop</c> alone, with a configuration that lists one service of each
/// provenance (LOCAL-SPEC.md §4.2): <c>sync</c> in process, <c>cli</c> external and <c>worker</c> spawned.
/// </summary>
internal static class LocalMeshes
{
    /// <summary>The node every local test talks to.</summary>
    public const string Node = "alice-laptop";

    /// <summary>The token the configuration holds for the external <c>cli</c>.</summary>
    public static ServiceToken CliToken { get; } = ServiceToken.FromBytes(Enumerable.Repeat((byte)0x11, 32).ToArray());

    /// <summary>The token the Host gave its spawned <c>worker</c>.</summary>
    public static ServiceToken WorkerToken { get; } = ServiceToken.FromBytes(Enumerable.Repeat((byte)0x22, 32).ToArray());

    /// <summary>A token nobody was given.</summary>
    public static ServiceToken WrongToken { get; } = ServiceToken.FromBytes(Enumerable.Repeat((byte)0x33, 32).ToArray());

    /// <summary>The configuration: one service of each provenance.</summary>
    public static LocalPortOptions Options { get; } = new()
    {
        Services =
        [
            new LocalService { Name = "sync", Provenance = ServiceProvenance.InProcess, Kinds = [PackageKind.Archive], EntryKinds = [EntryKind.Version] },
            new LocalService { Name = "cli", Provenance = ServiceProvenance.External, Token = CliToken, Kinds = [PackageKind.Archive, PackageKind.Channel], EntryKinds = [EntryKind.Version, EntryKind.Message, EntryKind.Redaction] },
            new LocalService { Name = "worker", Provenance = ServiceProvenance.Spawned, Token = WorkerToken },
        ],
    };

    /// <summary>Alice's laptop with its identity and the configuration above.</summary>
    public static Task<TestMesh> StartAsync() => StartAsync(Options);

    /// <summary>Alice's laptop with its identity and a configuration.</summary>
    public static Task<TestMesh> StartAsync(LocalPortOptions options) =>
        TestMesh.StartAsync(TestMeshOptions.Of(Node) with { Local = options }, CancellationToken.None);

    /// <summary>Alice's laptop as a fresh Host: its device key, no identity.</summary>
    public static Task<TestMesh> WithoutIdentityAsync() =>
        TestMesh.StartAsync(TestMeshOptions.Of(Node) with { Local = Options, WithoutIdentity = [Node] }, CancellationToken.None);
}
