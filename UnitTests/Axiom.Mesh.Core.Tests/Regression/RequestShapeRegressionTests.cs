using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;
using LocalPackageState = Axiom.Mesh.Local.PackageState;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>
/// The shapes the M9 requests are built from, pinned: how a list is cut to its count and frame, how a <c>Package</c> record
/// is made from trust, and how an operation's result body is encoded.
/// </summary>
public sealed class RequestShapeRegressionTests
{
    // Damage: a page holds one element too many or too few, or says more when nothing was left out.
    [Fact]
    public void APageTakesAtMostItsCount_AndSaysMoreOnlyWhenAnElementWasLeftOut()
    {
        int[] ten = [.. Enumerable.Range(1, 10)];

        (List<int> exact, bool exactMore) = PageCut.Take(ten, 10, long.MaxValue, _ => 1);
        (List<int> cut, bool cutMore) = PageCut.Take(ten, 4, long.MaxValue, _ => 1);
        (List<int> bytes, bool bytesMore) = PageCut.Take(ten, 10, 25, _ => 10);

        Assert.Equal((10, false), (exact.Count, exactMore));
        Assert.Equal([1, 2, 3, 4], cut);
        Assert.True(cutMore);
        Assert.Equal([1, 2], bytes);
        Assert.True(bytesMore);
    }

    // Damage: the Package record of a Package the node does not hold gains fields, or one it holds loses its Heads order,
    // its listedBy, or the rights a withdrawn own Grant takes away.
    [Fact]
    public void APackageRecordComesFromTrustHeadsCountAndListing()
    {
        VerifiedManifest manifest = Samples.Channel(TestKeys.Alice);
        PackageId id = manifest.Content.Package;
        Head[] heads = [new Head(TestIdentity.Of("bob-desktop").DeviceId, new ObjectHash(Hash32.Of([1])), 4), new Head(TestIdentity.Of("alice-laptop").DeviceId, new ObjectHash(Hash32.Of([2])), 7)];
        var held = new PackageTrust(id, PackageState.Followed, manifest, new Dictionary<UserId, VerifiedGrant>(), LocalRetention.LatestOnly);
        var trust = new TrustSnapshot(1, null, [], [held], new Dictionary<DeviceId, PublicKey>());

        Package known = PackageRecords.Of(id, trust, heads, 11, 70_000);
        Package unknown = PackageRecords.Of(PackageId.Derive(TestIdentity.Of("bob-desktop").UserId, default), trust, [], 0, 2);

        Assert.Equal((LocalPackageState.Followed, manifest.Content.Owner, "Family", LocalPolicy.LatestOnly, GrantRights.None), (known.State, known.OwnerId, known.Name, known.Policy, known.Rights));
        Assert.Equal(heads.OrderBy(h => h.Device.Value), known.Heads);
        Assert.Equal((11ul, 7ul, ushort.MaxValue), (known.Entries, known.LatestClock, known.ListedBy));
        Assert.Equal(new Package { PackageId = unknown.PackageId, State = LocalPackageState.Listed, ListedBy = 2 }, unknown);
    }

    // Damage: an operation's result body is encoded otherwise than its record (LOCAL-SPEC.md §8.6), which a client parses.
    [Fact]
    public void AResultBodyIsItsRecordsBytes()
    {
        byte[] bytes = RecordBytes.Of(new FetchResult { Objects = 3, Bytes = 258 });

        Assert.Equal(new byte[] { 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 1, 2 }, bytes);
    }
}
