using System.Collections.Generic;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §9.7 and §9.8: what a node may show the devices of another user.</summary>
public sealed class VisibilityRuleTests
{
    private static readonly UserId Alice = TestKeys.Alice.PublicKey.AsUserId();
    private static readonly UserId Bob = TestKeys.Bob.PublicKey.AsUserId();
    private static readonly UserId Eve = TestKeys.Eve.PublicKey.AsUserId();

    // §9.7: "A node MAY list, claim (Have), or send (Object) an object of a Package to a peer only if the peer's user is
    // the owner or holds a current Read or Write Grant … a Grant whose granteeId is the peer's user is always visible to
    // that peer, whatever its rights, so that a revocation reaches the revoked user. Packages.grants lists to the owner's
    // devices every current Grant, and to any other peer only the Grant whose granteeId is that peer's user."
    [Fact]
    public void APackageIsVisibleToItsOwnerAndCurrentGranteesOnly_ARevocationStillReachesItsGrantee()
    {
        var withoutGrant = new PackageAudience(Alice, new Dictionary<UserId, GrantRights>());
        var read = new PackageAudience(Alice, new Dictionary<UserId, GrantRights> { [Bob] = GrantRights.Read });
        var write = new PackageAudience(Alice, new Dictionary<UserId, GrantRights> { [Bob] = GrantRights.Write });
        var revoked = new PackageAudience(Alice, new Dictionary<UserId, GrantRights> { [Bob] = GrantRights.None });

        Assert.True(Visibility.MayList(Alice, withoutGrant));
        Assert.False(Visibility.MayList(Bob, withoutGrant));
        Assert.True(Visibility.MayList(Bob, read));
        Assert.True(Visibility.MayList(Bob, write));
        Assert.False(Visibility.MayList(Bob, revoked));
        Assert.False(Visibility.MayList(Eve, write));

        // The revocation Grant itself still reaches Bob; Eve never sees Bob's Grant; the owner sees every Grant.
        Assert.True(Visibility.MayClaimGrant(Bob, Alice, Bob));
        Assert.False(Visibility.MayClaimGrant(Eve, Alice, Bob));
        Assert.True(Visibility.MayClaimGrant(Alice, Alice, Bob));

        // §9.7: "An object held under several Packages is visible if any of them is."
        Assert.True(Visibility.MayServeThrough(Bob, [revoked, read]));
        Assert.False(Visibility.MayServeThrough(Bob, [revoked, withoutGrant]));
    }

    // §9.8: "A node serves a Roster … only for: the peer's own user, this node's own user, and the owner and current
    // grantees of Packages the peer may see. Any other RosterQuery is answered RosterNotFound."
    [Fact]
    public void ARosterIsServedForThePeersUserTheOwnUserAndTheAudienceOfPackagesThePeerMaySee()
    {
        var bobAndEve = new PackageAudience(Alice, new Dictionary<UserId, GrantRights> { [Bob] = GrantRights.Read, [Eve] = GrantRights.Write });
        var eveOnly = new PackageAudience(Alice, new Dictionary<UserId, GrantRights> { [Eve] = GrantRights.Read });
        UserId carol = new(RawObjects.Hash(0xCA).Value);

        // This node is Alice's; the peer is Bob's.
        Assert.True(Visibility.MayServeRoster(Bob, Bob, Alice, []));
        Assert.True(Visibility.MayServeRoster(Alice, Bob, Alice, []));
        Assert.True(Visibility.MayServeRoster(Eve, Bob, Alice, [bobAndEve]));
        Assert.False(Visibility.MayServeRoster(Eve, Bob, Alice, [eveOnly]));
        Assert.False(Visibility.MayServeRoster(carol, Bob, Alice, [bobAndEve, eveOnly]));
    }
}
