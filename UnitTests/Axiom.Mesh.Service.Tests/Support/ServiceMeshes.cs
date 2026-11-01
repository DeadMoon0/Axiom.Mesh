using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Service.Tests.Support;

/// <summary>
/// The meshes the runtime's tests start: <c>alice-laptop</c> alone, its local port listing <c>cli</c> and the echo service
/// (<see cref="TestEchoService"/>) as external services, and helpers to write Entries of the echo service's kind.
/// </summary>
internal static class ServiceMeshes
{
    public const string Node = "alice-laptop";

    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>Alice's laptop, configured for the echo service.</summary>
    public static Task<TestMesh> StartAsync() =>
        TestMesh.StartAsync(TestMeshOptions.Of(Node) with { Local = TestServices.With(TestCliService.Local, TestEchoService.Echo) }, None);

    /// <summary>A Package of the echo service's kind, owned and followed by the node.</summary>
    public static Task<PackageId> NotesAsync(TestMesh mesh) => mesh[Node].CreatePackageAsync("Notes", TestEchoService.Kind, Retention.Default, None);

    /// <summary>Writes Entries of the echo service's kind, in order.</summary>
    public static async Task<IReadOnlyList<ObjectHash>> WriteAsync(TestMesh mesh, PackageId package, int count)
    {
        var written = new List<ObjectHash>(count);
        for (int i = 0; i < count; i++)
            written.Add(await mesh[Node].WriteEntryAsync(package, new EntryBody(TestEchoService.EntryKind, new byte[] { (byte)i, (byte)(i >> 8) }, EntryReferences.None, default), None));
        return written;
    }

    /// <summary>The environment the echo service runs in beside the node, its state on <paramref name="storage"/>.</summary>
    public static ServiceEnvironment EnvironmentOf(TestMesh mesh, InMemoryStorageEnvironment storage, string name) => new()
    {
        Version = "test",
        ConnectHost = _ => Task.FromResult(mesh[Node].ConnectRaw(TestProcess.Served)),
        HostAddress = "the in-memory node " + Node,
        OpenState = ct => Database.OpenAsync(storage, DatabaseOptions.Default with { TimeProvider = mesh.Clock }, ct),
        ReadConfig = () => TestServices.ConfigOf(name),
        Time = mesh.Clock,
    };

    /// <summary>Waits until a running service connected, or fails when it exited first.</summary>
    public static async Task<RunningService> ConnectedAsync(RunningService running)
    {
        await running.WhenConnectedAsync(None);
        return running;
    }

    /// <summary>A user client's call to the echo service.</summary>
    public static ValueTask<LocalResult<ReadOnlyMemory<byte>>> CallAsync(LocalClient client, string method, byte[] payload) =>
        client.SendAsync(new ServiceCall { Service = TestEchoService.Echo.Name, Method = method, Payload = payload }, None);
}
