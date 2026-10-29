using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §6.1 and §6 header over the local port: <c>CreateIdentity</c> makes the user key and a Roster with seq 1
/// holding this device and swaps the device credential in; twice is <c>AlreadyExists</c>; before it the identity is not
/// present and the requests that need one are <c>NoIdentity</c>; <c>RenameDevice</c> makes the next Roster.
/// </summary>
public sealed class IdentityRequestTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task CreateIdentityMakesRosterSeq1WithThisDevice_TwiceIsAlreadyExists()
    {
        await using TestMesh mesh = await LocalMeshes.WithoutIdentityAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient client = await node.ConnectUserAsync("cli", None);
        Assert.False(client.Host.Identity.Present);
        Assert.False((await client.SendAsync(new GetIdentity(), None)).Value.Present);
        Assert.Equal(Status.NoIdentity, (await client.SendAsync(new RenameDevice { DeviceId = node.Device, Name = "x" }, None)).Status);
        Assert.Equal(Status.Invalid, (await client.SendAsync(new CreateIdentity { UserName = "", DeviceName = "laptop" }, None)).Status);
        Task<NodeIdentity> known = node.Node.WhenIdentityAsync(None);
        Assert.False(known.IsCompleted);

        Identity created = (await client.SendAsync(new CreateIdentity { UserName = "Alice", DeviceName = "laptop" }, None)).Value;

        Assert.True(created.Present);
        Assert.Equal("Alice", created.UserName);
        Assert.Equal("laptop", created.DeviceName);
        Assert.Equal(1ul, created.RosterSeq);
        Assert.True(created.UserKeyPresent);
        Assert.Equal(node.Device, created.DeviceId);
        Assert.Equal(mesh.Clock.GetUtcNow().ToUnixTimeMilliseconds(), (long)created.Created);
        TrustSnapshot trust = node.Node.Trust;
        Assert.True(trust.TryGetUser(created.UserId, out UserTrust? user));
        Assert.Equal(1ul, user.Roster!.Content.Seq);
        Assert.Equal([node.Device], user.Roster.Content.Devices.Select(d => d.Id));
        Assert.Equal(created.UserId, (await known.WaitAsync(TestMesh.Deadline)).User);
        Assert.NotNull(await node.Environment.Keys.ReadAsync(KeyRole.User, None));

        Assert.Equal(Status.AlreadyExists, (await client.SendAsync(new CreateIdentity { UserName = "Alice", DeviceName = "laptop" }, None)).Status);
        LocalClient later = await node.ConnectUserAsync("app", None);
        Assert.Equal(created, later.Host.Identity);

        Device renamed = (await client.SendAsync(new RenameDevice { DeviceId = node.Device, Name = "desk" }, None)).Value;
        Assert.Equal("desk", renamed.Name);
        Assert.True(renamed.Self);
        Identity after = (await client.SendAsync(new GetIdentity(), None)).Value;
        Assert.Equal(2ul, after.RosterSeq);
        Assert.Equal("desk", after.DeviceName);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new RenameDevice { DeviceId = TestIdentity.Of("bob-desktop").DeviceId, Name = "x" }, None)).Status);
    }
}
