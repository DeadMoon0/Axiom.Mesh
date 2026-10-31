using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Integration.Tests.Rules;

/// <summary>
/// SPEC.md §3.3 on sockets: a static peer is an address the user names, without discovery; the node dials it, and the
/// handshake decides who answers. Two devices that never hear each other's announcement connect once one names the other.
/// </summary>
public sealed class StaticPeerTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task ADeviceNamedAsAStaticPeerOnTheCommandLineIsDialledAndEstablished()
    {
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync(["alice-laptop", "alice-phone"], [], [("alice-laptop", "alice-phone")], None);
        string phone = mesh.Node("alice-phone").MeshEndpoint.ToString();

        CliResult added = await mesh.CliAsync("alice-laptop", "peer add " + phone, None);
        await mesh.WaitUntilAsync("alice-laptop", n => MeshWaits.IsEstablishedWith(n, TestIdentity.Of("alice-phone").DeviceId), "the laptop established with the phone it named", None);

        Assert.True(added.ExitCode == Axiom.Mesh.Cli.CliExitCode.Ok, added.ToString());
        Assert.True(MeshWaits.IsEstablishedWith(mesh.Node("alice-phone"), TestIdentity.Of("alice-laptop").DeviceId));
    }
}
