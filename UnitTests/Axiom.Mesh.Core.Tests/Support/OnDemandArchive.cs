using System;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>
/// Alice's laptop owns an Archive granted to Bob with two Versions, an older folder and a newer one. Bob follows it
/// latest-only: he holds both Version Entries and the newer folder, not the older one, whose Tree the older Version
/// references. Then Alice's laptop stops, so nothing can supply the older folder until it starts again.
/// </summary>
internal sealed class OnDemandArchive : IAsyncDisposable
{
    private static readonly CancellationToken None = CancellationToken.None;

    private OnDemandArchive(TestMesh mesh, PackageId archive, TreeFixture older)
    {
        Mesh = mesh;
        Archive = archive;
        Older = older;
    }

    public TestMesh Mesh { get; }

    public PackageId Archive { get; }

    /// <summary>The folder of the older Version: its Entry is stored at Bob, its objects are not.</summary>
    public TreeFixture Older { get; }

    public TestNode Bob => Mesh["bob-desktop"];

    public static async Task<OnDemandArchive> StartAsync()
    {
        TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop") with { Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop"), Silent = ["alice-laptop", "bob-desktop"] }, None);
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        TreeFixture older = ObjectFixtures.Folder(("a.txt", ObjectFixtures.Content(3_000, 1)), ("b.bin", ObjectFixtures.Content(Limits.ChunkLength + 500, 2)));
        TreeFixture newer = ObjectFixtures.Folder(("a.txt", ObjectFixtures.Content(3_000, 3)));
        PackageId archive = await alice.CreatePackageAsync("Photos", PackageKind.Archive, Retention.Default, None);
        await alice.GrantAsync(archive, "bob", GrantRights.Read, None);
        ObjectHash first = await alice.PublishAsync(archive, older, None);
        await alice.PublishAsync(archive, newer, None);
        await bob.FollowAsync(archive, None);
        await bob.Node.Writer.SendAsync(new PackageCommand(PackageCommandKind.SetPolicy, archive, LocalRetention.LatestOnly), None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await bob.WaitForEntryAsync(first, None);
        await bob.WaitForObjectAsync(newer.Root.Hash, None);
        await mesh.SettleAsync(None);
        Assert.False(await bob.Node.ContainsAsync(older.Root.Hash, None));
        await mesh.StopNodeAsync("alice-laptop", None);
        return new OnDemandArchive(mesh, archive, older);
    }

    public ValueTask DisposeAsync() => Mesh.DisposeAsync();
}
