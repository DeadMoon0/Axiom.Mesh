using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Sync.Tests.Support;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Sync.Tests.Conventions;

/// <summary>Every method the Client library names has a handler in the service: none answers <c>UnknownMethod</c>.</summary>
public sealed class MethodTableTests
{
    [Fact]
    public async Task EveryMethodOfTheTableHasAHandler()
    {
        await using TestMesh mesh = await SyncMeshes.StartAsync(SyncMeshes.Alice);
        await using RunningService sync = await SyncMeshes.RunAsync(mesh, SyncMeshes.Alice);
        await using LocalClient app = await mesh[SyncMeshes.Alice].ConnectUserAsync("sync-table", CancellationToken.None);
        string[] methods = [.. typeof(SyncMethods).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!)];

        foreach (string method in methods)
        {
            LocalResult<System.ReadOnlyMemory<byte>> answered = await app.SendAsync(new ServiceCall { Service = SyncProtocol.ServiceName, Method = method, Payload = new byte[] { 0xff } }, CancellationToken.None);
            Assert.NotEqual((Status)(ushort)SyncStatus.UnknownMethod, answered.Status);
        }

        Assert.Equal(6, methods.Length);
    }
}
