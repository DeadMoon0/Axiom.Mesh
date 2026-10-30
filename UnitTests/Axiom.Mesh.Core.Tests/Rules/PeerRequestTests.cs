using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §6.6 and §5.9: an added static peer is listed and dialled, and its record says when and how; adding it
/// twice is <c>AlreadyExists</c>, no <c>host:port</c> is <c>Invalid</c>, removing an unknown one is <c>NotFound</c>; <c>Dial</c>
/// with both fields empty is <c>Invalid</c>, an unknown device <c>NotFound</c>, an established one <c>AlreadyExists</c>.
/// </summary>
public sealed class PeerRequestTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §6.6, §5.9: two silent nodes; Alice adds Bob's address as a static peer, which is dialled and establishes.
    [Fact]
    public async Task AnAddedStaticPeerIsListedAndDialled_AndItsRecordSaysEstablished()
    {
        await using TestMesh mesh = await SilentPairAsync();
        TestNode alice = mesh["alice-laptop"];
        string address = mesh["bob-desktop"].Address.ToString();
        LocalClient client = await alice.ConnectUserAsync("app", None);

        StaticPeer added = (await client.SendAsync(new AddStaticPeer { Address = address }, None)).Value;
        await mesh.WaitUntilAsync("alice-laptop", n => n.Table.EstablishedWith(mesh["bob-desktop"].Device) is not null, "Alice established with Bob through the static peer", None);

        Assert.Equal(address, added.Address);
        StaticPeer listed = Assert.Single((await client.SendAsync(new ListStaticPeers(), None)).Value);
        Assert.Equal(address, listed.Address);
        Assert.Equal(DialResult.Established, listed.LastResult);
        Assert.Equal(LocalRecords.Milliseconds(mesh.Clock.GetUtcNow()), listed.LastDialled);
        Assert.Equal(Status.AlreadyExists, (await client.SendAsync(new AddStaticPeer { Address = address }, None)).Status);
    }

    // §6.6: no host:port is Invalid; removing an address that is not a static peer is NotFound; a removed one is gone.
    [Fact]
    public async Task NoHostPortIsInvalid_RemovingAnUnknownAddressIsNotFound()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        LocalClient client = await mesh["alice-laptop"].ConnectUserAsync("app", None);

        Assert.Equal(Status.Invalid, (await client.SendAsync(new AddStaticPeer { Address = "no port" }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new RemoveStaticPeer { Address = "192.0.2.1:41810" }, None)).Status);
        Assert.True((await client.SendAsync(new AddStaticPeer { Address = "192.0.2.1:41810" }, None)).IsOk);
        Assert.True((await client.SendAsync(new RemoveStaticPeer { Address = "192.0.2.1:41810" }, None)).IsOk);
        Assert.Empty((await client.SendAsync(new ListStaticPeers(), None)).Value);
    }

    // §6.6: Dial with both fields empty or an address that is no IP:port is Invalid; an unknown device is NotFound; a
    // known device at its address is dialled; dialling it again once established is AlreadyExists.
    [Fact]
    public async Task DialRefusesEmptyAndUnknown_DialsAKnownDevice_AndAnEstablishedOneIsAlreadyExists()
    {
        await using TestMesh mesh = await SilentPairAsync();
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        LocalClient client = await alice.ConnectUserAsync("app", None);

        Assert.Equal(Status.Invalid, (await client.SendAsync(new Dial(), None)).Status);
        Assert.Equal(Status.Invalid, (await client.SendAsync(new Dial { DeviceId = bob.Device, Address = "bob" }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new Dial { DeviceId = TestIdentity.Of("eve-device").DeviceId }, None)).Status);
        Assert.True((await client.SendAsync(new Dial { DeviceId = bob.Device, Address = bob.Address.ToString() }, None)).IsOk);
        await mesh.WaitUntilAsync("alice-laptop", n => n.Table.EstablishedWith(bob.Device) is not null, "Alice established with Bob", None);

        Assert.Equal(Status.AlreadyExists, (await client.SendAsync(new Dial { DeviceId = bob.Device }, None)).Status);
        Assert.Empty((await client.SendAsync(new ListStaticPeers(), None)).Value);
    }

    private static Task<TestMesh> SilentPairAsync() => TestMesh.StartAsync(
        TestMeshOptions.Of("alice-laptop", "bob-desktop") with
        {
            Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop"),
            Silent = ["alice-laptop", "bob-desktop"],
        },
        None);
}
