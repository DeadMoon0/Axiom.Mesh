using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §6.4, the Grant requests: a Grant's seq is one above the current Grant for the grantee, or 1; the owner
/// only (<c>NotOwner</c>), with the user key (<c>UserKeyAbsent</c>); the grantee a known user (<c>NotFound</c>) other than
/// the owner (<c>Invalid</c>); <c>RevokeGrant</c> is <c>NotFound</c> without a current Grant; <c>ListGrants</c> ascending by
/// grantee. (The revocation reaching the revoked user is <see cref="ServingTests"/>.)
/// </summary>
public sealed class GrantRequestTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly UserId Bob = TestIdentity.Of("bob-desktop").UserId;

    // §6.4: the first Grant for a grantee has seq 1, the next 2; RevokeGrant is SetGrant with rights 0 and seq 3.
    [Fact]
    public async Task GrantsCountUpFrom1PerGrantee_AndRevokeGrantIsRights0()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestNode laptop = mesh["alice-laptop"];
        await laptop.HoldUserKeyAsync(None);
        LocalClient client = await laptop.ConnectUserAsync("app", None);
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);

        Grant first = (await client.SendAsync(new SetGrant { PackageId = channel, GranteeId = Bob, Rights = GrantRights.Read }, None)).Value;
        Grant second = (await client.SendAsync(new SetGrant { PackageId = channel, GranteeId = Bob, Rights = GrantRights.Write }, None)).Value;
        Grant revoked = (await client.SendAsync(new RevokeGrant { PackageId = channel, GranteeId = Bob }, None)).Value;

        Assert.Equal((1ul, GrantRights.Read), (first.Seq, first.Rights));
        Assert.Equal((2ul, GrantRights.Write), (second.Seq, second.Rights));
        Assert.Equal((3ul, GrantRights.None), (revoked.Seq, revoked.Rights));
        Assert.Equal([revoked], (await client.SendAsync(new ListGrants { PackageId = channel }, None)).Value);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new RevokeGrant { PackageId = channel, GranteeId = Bob }, None)).Status);
    }

    // §6.4: the owner as grantee is Invalid; a user who is not known is NotFound; rights above 2 are Invalid.
    [Fact]
    public async Task TheOwnerAsGranteeIsInvalid_AnUnknownUserIsNotFound_RightsAbove2AreInvalid()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode laptop = mesh["alice-laptop"];
        await laptop.HoldUserKeyAsync(None);
        LocalClient client = await laptop.ConnectUserAsync("app", None);
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);

        Assert.Equal(Status.Invalid, (await client.SendAsync(new SetGrant { PackageId = channel, GranteeId = laptop.User, Rights = GrantRights.Read }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new SetGrant { PackageId = channel, GranteeId = Bob, Rights = GrantRights.Read }, None)).Status);
        Assert.Equal(Status.Invalid, (await client.SendAsync(new SetGrant { PackageId = channel, GranteeId = Bob, Rights = (GrantRights)3 }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new ListGrants { PackageId = PackageId.Derive(Bob, default) }, None)).Status);
        Assert.Empty((await client.SendAsync(new ListGrants { PackageId = channel }, None)).Value);
    }

    // §6.4: a Package of another owner is NotOwner; without the user key a Grant is UserKeyAbsent.
    [Fact]
    public async Task AnotherOwnersPackageIsNotOwner_WithoutTheUserKeyItIsUserKeyAbsent()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop") with { Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop") }, None);
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        PackageId channel = await alice.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await alice.GrantAsync(channel, "bob", GrantRights.Write, None);
        await bob.FollowAsync(channel, None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await Waits.UntilAsync(() => bob.Node.Trust.TryGetGrant(channel, bob.User, out _), "Bob to hold his Grant");
        await bob.HoldUserKeyAsync(None);
        LocalClient atBob = await bob.ConnectUserAsync("app", None);
        LocalClient atAlice = await alice.ConnectUserAsync("app", None);

        Assert.Equal(Status.NotOwner, (await atBob.SendAsync(new SetGrant { PackageId = channel, GranteeId = alice.User, Rights = GrantRights.Read }, None)).Status);
        Assert.Equal(Status.UserKeyAbsent, (await atAlice.SendAsync(new SetGrant { PackageId = channel, GranteeId = bob.User, Rights = GrantRights.Read }, None)).Status);
        Assert.Equal(GrantRights.Write, Assert.Single((await atBob.SendAsync(new ListGrants { PackageId = channel }, None)).Value).Rights);
        Assert.Equal(GrantRights.Write, (await atBob.SendAsync(new GetPackage { PackageId = channel }, None)).Value.Rights);
        Assert.True(bob.Node.Trust.TryGetGrant(channel, bob.User, out VerifiedGrant? held) && held.Content.Seq == 1);
        Assert.Single(alice.Node.Trust.Packages.Single(p => p.Id == channel).Grants);
    }
}
