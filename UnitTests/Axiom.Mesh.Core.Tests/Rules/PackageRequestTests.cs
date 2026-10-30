using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using LocalPackageState = Axiom.Mesh.Local.PackageState;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §6.4, the Package requests: <c>CreatePackage</c> makes a followed Package with Manifest seq 1 owned by
/// the own user; <c>ListPackages</c> pages ascending by id with its filters; <c>GetPackage</c>, <c>FollowPackage</c>,
/// <c>UnfollowPackage</c> and <c>SetPackagePolicy</c> with every refusal the section lists.
/// </summary>
public sealed class PackageRequestTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §6.4: CreatePackage produces Manifest seq 1, owned by the own user, followed, policy 0, rights Write.
    [Fact]
    public async Task CreatePackageMakesAFollowedPackageOwnedHereWithManifestSeq1()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode laptop = mesh["alice-laptop"];
        await laptop.HoldUserKeyAsync(None);
        LocalClient client = await laptop.ConnectUserAsync("app", None);

        Package created = (await client.SendAsync(new CreatePackage { Kind = PackageKind.Channel, Name = "Family", Retention = Retention.Everything }, None)).Value;

        Assert.Equal(LocalPackageState.Followed, created.State);
        Assert.Equal(laptop.User, created.OwnerId);
        Assert.Equal(PackageKind.Channel, created.Kind);
        Assert.Equal("Family", created.Name);
        Assert.Equal(Retention.Everything, created.Retention);
        Assert.Equal(1ul, created.ManifestSeq);
        Assert.Equal(LocalPolicy.Default, created.Policy);
        Assert.Equal(GrantRights.Write, created.Rights);
        Assert.Equal(PackageId.Derive(laptop.User, Nonce(laptop, created.PackageId)), created.PackageId);
        Assert.Equal(created, (await client.SendAsync(new GetPackage { PackageId = created.PackageId }, None)).Value);
    }

    // §6.4: an empty name is Invalid; without the user key CreatePackage is UserKeyAbsent and nothing is created.
    [Fact]
    public async Task AnEmptyNameIsInvalid_WithoutTheUserKeyItIsUserKeyAbsent()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        LocalClient client = await mesh["alice-laptop"].ConnectUserAsync("app", None);

        Assert.Equal(Status.Invalid, (await client.SendAsync(new CreatePackage { Kind = PackageKind.Channel, Name = "" }, None)).Status);
        Assert.Equal(Status.UserKeyAbsent, (await client.SendAsync(new CreatePackage { Kind = PackageKind.Channel, Name = "Family" }, None)).Status);
        Assert.Empty((await client.SendAsync(new ListPackages(), None)).Value.Packages);
    }

    // §6.4: ListPackages pages ascending by id after the cursor, filters by kind and owner, and says more when it cut.
    [Fact]
    public async Task ListPackagesPagesAscendingAfterTheCursorAndFiltersByKind()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode laptop = mesh["alice-laptop"];
        LocalClient client = await laptop.ConnectUserAsync("app", None);
        PackageId[] ids = [
            await laptop.CreatePackageAsync("One", PackageKind.Channel, Retention.Default, None),
            await laptop.CreatePackageAsync("Two", PackageKind.Archive, Retention.Default, None),
            await laptop.CreatePackageAsync("Three", PackageKind.Channel, Retention.Default, None),
        ];
        PackageId[] ordered = [.. ids.Order()];

        PackagePage all = (await client.SendAsync(new ListPackages(), None)).Value;
        PackagePage afterFirst = (await client.SendAsync(new ListPackages { After = ordered[0] }, None)).Value;
        PackagePage channels = (await client.SendAsync(new ListPackages { Kinds = [PackageKind.Channel] }, None)).Value;
        PackagePage strangers = (await client.SendAsync(new ListPackages { OwnerId = TestIdentity.Of("bob-desktop").UserId }, None)).Value;

        Assert.Equal(ordered, all.Packages.Select(p => p.PackageId));
        Assert.False(all.More);
        Assert.Equal(ordered[1..], afterFirst.Packages.Select(p => p.PackageId));
        Assert.Equal(ids.Where((_, i) => i != 1).Order(), channels.Packages.Select(p => p.PackageId));
        Assert.Empty(strangers.Packages);
        Assert.Equal(Status.Invalid, (await client.SendAsync(new ListPackages { Kinds = [PackageKind.Channel, PackageKind.Archive] }, None)).Status);
    }

    // §6.4: an unknown Package is NotFound for GetPackage, FollowPackage, UnfollowPackage and SetPackagePolicy.
    [Fact]
    public async Task AnUnknownPackageIsNotFound()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        LocalClient client = await mesh["alice-laptop"].ConnectUserAsync("app", None);
        PackageId unknown = PackageId.Derive(TestIdentity.Of("bob-desktop").UserId, new PackageNonce(Hash32.Of([7])));

        Assert.Equal(Status.NotFound, (await client.SendAsync(new GetPackage { PackageId = unknown }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new FollowPackage { PackageId = unknown }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new UnfollowPackage { PackageId = unknown }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new SetPackagePolicy { PackageId = unknown, Policy = LocalPolicy.Everything }, None)).Status);
    }

    // §6.4: the own Package cannot be unfollowed (Invalid); FollowPackage on a followed one sets the policy and nothing
    // else; a policy outside §14.14 is Invalid.
    [Fact]
    public async Task TheOwnPackageCannotBeUnfollowed_FollowingAgainSetsThePolicy_AnUnknownPolicyIsInvalid()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode laptop = mesh["alice-laptop"];
        LocalClient client = await laptop.ConnectUserAsync("app", None);
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);

        Assert.Equal(Status.Invalid, (await client.SendAsync(new UnfollowPackage { PackageId = channel }, None)).Status);
        Package followed = (await client.SendAsync(new FollowPackage { PackageId = channel, Policy = LocalPolicy.LatestOnly }, None)).Value;
        Package policy = (await client.SendAsync(new SetPackagePolicy { PackageId = channel, Policy = LocalPolicy.Everything }, None)).Value;

        Assert.Equal(LocalPackageState.Followed, followed.State);
        Assert.Equal(LocalPolicy.LatestOnly, followed.Policy);
        Assert.Equal(LocalPolicy.Everything, policy.Policy);
        Assert.Equal(Status.Invalid, (await client.SendAsync(new SetPackagePolicy { PackageId = channel, Policy = (LocalPolicy)3 }, None)).Status);
    }

    // §5.4, §6.4: a Package a peer lists is Listed with listedBy 1; FollowPackage makes it Followed; UnfollowPackage Listed again.
    [Fact]
    public async Task APackageAPeerListsIsListedByIt_FollowedOnRequest_AndListedAgainAfterUnfollow()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop") with { Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop") }, None);
        TestNode alice = mesh["alice-laptop"];
        PackageId channel = await alice.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await alice.GrantAsync(channel, "bob", GrantRights.Read, None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await mesh.SettleAsync(None);
        LocalClient bob = await mesh["bob-desktop"].ConnectUserAsync("app", None);

        Package listed = (await bob.SendAsync(new GetPackage { PackageId = channel }, None)).Value;
        Package followed = (await bob.SendAsync(new FollowPackage { PackageId = channel }, None)).Value;
        Package unfollowed = (await bob.SendAsync(new UnfollowPackage { PackageId = channel }, None)).Value;

        Assert.Equal(LocalPackageState.Listed, listed.State);
        Assert.Equal((ushort)1, listed.ListedBy);
        Assert.Equal(LocalPackageState.Followed, followed.State);
        Assert.Equal(LocalPackageState.Listed, unfollowed.State);
        Assert.Equal([channel], (await bob.SendAsync(new ListPackages { FollowedOnly = false }, None)).Value.Packages.Select(p => p.PackageId));
        Assert.Empty((await bob.SendAsync(new ListPackages { FollowedOnly = true }, None)).Value.Packages);
    }

    private static PackageNonce Nonce(TestNode node, PackageId package) =>
        node.Node.Trust.TryGetManifest(package, out VerifiedManifest? manifest) ? manifest.Content.Nonce : default;
}
