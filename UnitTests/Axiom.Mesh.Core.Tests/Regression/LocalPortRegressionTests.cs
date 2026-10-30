using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>What the local port says today, pinned: the <c>HelloAck</c> of a fresh node and of one with an identity, and the log line of a user-key request.</summary>
public sealed class LocalPortRegressionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // Damage: HelloAck of a Host without an identity changes shape (capabilities, version, store fields), which every client reads first.
    [Fact]
    public async Task TheHelloAckOfAFreshHostSaysNoIdentityStoreSeq0AndNoCapabilities()
    {
        await using TestMesh mesh = await LocalMeshes.WithoutIdentityAsync();
        LocalClient client = await mesh[LocalMeshes.Node].ConnectUserAsync("snapshot", None);
        HelloAck ack = client.Host;

        Assert.Equal(1, ack.Version);
        Assert.Equal("axiom-mesh", ack.HostVersion);
        Assert.Equal(Role.User, ack.Role);
        Assert.Equal(HostCapabilities.None, ack.Capabilities);
        Assert.Equal(new Identity(), ack.Identity);
        Assert.False(ack.StoreEpoch.IsZero);
        Assert.Equal(0ul, ack.StoreSeq);
    }

    // Damage: a connection of another OS user than the Host's loses capability bit 1, or one of the served user gains it.
    [Fact]
    public async Task OnlyAConnectionOfAnotherUserGetsHostCapabilityBit1()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient served = await node.ConnectUserAsync("served", None);
        LocalClient other = await node.ConnectAsync(new LocalClientOptions { Name = "other" }, TestProcess.Served.AsOtherUser(), None);

        Assert.Equal(HostCapabilities.None, served.Host.Capabilities);
        Assert.Equal(HostCapabilities.OtherAccount, other.Host.Capabilities);
        Assert.True(served.Host.Identity.Present);
        Assert.Equal(mesh[LocalMeshes.Node].Device, served.Host.Identity.DeviceId);
    }

    // Damage: the user-key log line loses the process id, the executable, the user or the outcome that §2.2 requires.
    [Fact]
    public async Task AUserKeyRequestIsLoggedInOneLineWithNamePidExecutableUserConnectionAndStatus()
    {
        var log = new CapturedLog();
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of(LocalMeshes.Node) with { Loggers = log }, None);
        LocalClient client = await mesh[LocalMeshes.Node].ConnectUserAsync("app", None);
        await client.SendAsync(new ForgetUser { UserId = default }, None);

        string line = Assert.Single(log.Lines.Where(l => l.Category == "Axiom.Mesh.Local").Select(l => l.Message));
        Assert.Matches("^user-key request ForgetUser \\(277\\) from pid 4242 \"/usr/bin/axiom-test\" as \"tester\" on local connection #\\d+: NotFound$", line);
    }
}
