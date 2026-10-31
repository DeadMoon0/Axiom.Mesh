using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §6.2 (known users, <c>ForgetUser</c>) and §6.3 (devices) over the local port: lists in their stated
/// order, a forgotten user is no longer known, a removed device stands removed in the next Roster, and every refusal
/// the sections list.
/// </summary>
public sealed class UserRequestTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly TestIdentity Alice = TestIdentity.Of("alice-laptop");
    private static readonly TestIdentity Bob = TestIdentity.Of("bob-desktop");

    // §6.2: ListKnownUsers holds every known user, the own one included, ascending by user id; the own user has its name.
    [Fact]
    public async Task KnownUsersAreListedAscendingWithTheOwnUserIncluded()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        LocalClient client = await mesh["alice-laptop"].ConnectUserAsync("app", None);

        IReadOnlyList<User> users = (await client.SendAsync(new ListKnownUsers(), None)).Value;

        Assert.Equal(new[] { Alice.UserId, Bob.UserId }.OrderBy(u => u.Value), users.Select(u => u.UserId));
        User own = users.Single(u => u.Self);
        Assert.Equal(Alice.UserId, own.UserId);
        Assert.Equal(1ul, users.Single(u => !u.Self).RosterSeq);
        Assert.Equal((ushort)1, users.Single(u => !u.Self).Devices);
    }

    // §6.2: ForgetUser drops the user: it is no longer listed, its devices no longer listable; twice is NotFound.
    [Fact]
    public async Task AForgottenUserIsNoLongerKnown_ForgettingAgainIsNotFound()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        LocalClient client = await mesh["alice-laptop"].ConnectUserAsync("app", None);

        Assert.True((await client.SendAsync(new ForgetUser { UserId = Bob.UserId }, None)).IsOk);

        Assert.Equal([Alice.UserId], (await client.SendAsync(new ListKnownUsers(), None)).Value.Select(u => u.UserId));
        Assert.Equal(Status.NotFound, (await client.SendAsync(new ListDevices { UserId = Bob.UserId }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new ForgetUser { UserId = Bob.UserId }, None)).Status);
        Assert.False(mesh["alice-laptop"].Node.Trust.TryResolveDevice(Bob.Device.PublicKey.Encoded, out _, out _));
    }

    // §6.2, §14.11 (debt 44): forgetting a user removes its devices from the peer table; topic peers says Forgotten for each.
    [Fact]
    public async Task ForgettingAUserForgetsItsDevicesAsPeers_TopicPeersSaysForgotten()
    {
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        LocalClient client = await mesh["alice-laptop"].ConnectUserAsync("app", None);
        Subscription<PeerChanged> peers = (await client.SubscribeAsync<PeerChanged>(Filter.Any, None)).Value;

        Assert.True((await client.SendAsync(new ForgetUser { UserId = Bob.UserId }, None)).IsOk);

        PeerChanged forgotten = await Events.FirstAsync(peers, e => e.Change == PeerChange.Forgotten);
        Assert.Equal(Bob.DeviceId, forgotten.Peer.DeviceId);
        Assert.DoesNotContain(mesh["alice-laptop"].Node.Peers, p => p.Device == Bob.DeviceId);
        Assert.DoesNotContain((await client.SendAsync(new ListPeers(), None)).Value, p => p.DeviceId == Bob.DeviceId);
    }

    // §6.2: the own user, and a user who holds a current Grant from the own user, cannot be forgotten.
    [Fact]
    public async Task TheOwnUserAndAGranteeOfTheOwnUserCannotBeForgotten()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode laptop = mesh["alice-laptop"];
        LocalClient client = await laptop.ConnectUserAsync("app", None);
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await laptop.GrantAsync(channel, "bob", GrantRights.Read, None);

        Assert.Equal(Status.Invalid, (await client.SendAsync(new ForgetUser { UserId = Alice.UserId }, None)).Status);
        Assert.Equal(Status.Invalid, (await client.SendAsync(new ForgetUser { UserId = Bob.UserId }, None)).Status);
        Assert.Contains(Bob.UserId, (await client.SendAsync(new ListKnownUsers(), None)).Value.Select(u => u.UserId));
    }

    // §6.3: ListDevices with a zero id lists the own user's devices ascending, removed ones included; an unknown user is NotFound.
    [Fact]
    public async Task DevicesOfTheOwnUserAreListedAscending_AnUnknownUserIsNotFound()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        LocalClient client = await mesh["alice-laptop"].ConnectUserAsync("app", None);

        IReadOnlyList<Device> devices = (await client.SendAsync(new ListDevices(), None)).Value;

        TestIdentity[] own = [.. TestIdentity.All.Where(i => i.UserName == "alice").OrderBy(i => i.DeviceId.Value)];
        Assert.Equal(own.Select(i => i.DeviceId), devices.Select(d => d.DeviceId));
        Assert.Equal(Alice.DeviceId, devices.Single(d => d.Self).DeviceId);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new ListDevices { UserId = Bob.UserId }, None)).Status);
    }

    // §6.3: RemoveDevice makes a Roster with seq one higher marking the device removed; the device stays listed, removed.
    [Fact]
    public async Task RemovingADeviceMakesTheNextRosterWithTheDeviceRemoved()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode laptop = mesh["alice-laptop"];
        await laptop.HoldUserKeyAsync(None);
        LocalClient client = await laptop.ConnectUserAsync("app", None);
        DeviceId phone = TestIdentity.Of("alice-phone").DeviceId;

        Device removed = (await client.SendAsync(new RemoveDevice { DeviceId = phone }, None)).Value;

        Assert.True(removed.Removed);
        Assert.Equal(phone, removed.DeviceId);
        Assert.Equal(2ul, (await client.SendAsync(new GetIdentity(), None)).Value.RosterSeq);
        Assert.True((await client.SendAsync(new ListDevices(), None)).Value.Single(d => d.DeviceId == phone).Removed);
        Assert.True(laptop.Node.Trust.IsRemoved(phone));
    }

    // §6.3: RemoveDevice is Invalid for this device, NotFound for another user's device, UserKeyAbsent without the user key.
    [Fact]
    public async Task RemovingThisDeviceIsInvalid_AnotherUsersIsNotFound_WithoutTheUserKeyItIsUserKeyAbsent()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        LocalClient client = await mesh["alice-laptop"].ConnectUserAsync("app", None);
        DeviceId phone = TestIdentity.Of("alice-phone").DeviceId;

        Assert.Equal(Status.Invalid, (await client.SendAsync(new RemoveDevice { DeviceId = Alice.DeviceId }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new RemoveDevice { DeviceId = Bob.DeviceId }, None)).Status);
        Assert.Equal(Status.UserKeyAbsent, (await client.SendAsync(new RemoveDevice { DeviceId = phone }, None)).Status);
        Assert.False(mesh["alice-laptop"].Node.Trust.IsRemoved(phone));
    }
}
