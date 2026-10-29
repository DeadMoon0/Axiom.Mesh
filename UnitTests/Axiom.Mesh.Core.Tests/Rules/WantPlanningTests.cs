using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Tests.Support;
using WireState = Axiom.Mesh.Protocol.PackageState;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// What a node wants (SPEC.md §10.2, §10.3, §11.2), from the spec's steps: the Manifest first and alone when none is
/// held, the Grants it lacks, Heads only from devices that may write and only when newer; then parents until held
/// Entries, and payload objects as far as the retention row goes.
/// </summary>
public sealed class WantPlanningTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly VerifiedManifest Channel = Samples.Channel(TestKeys.Alice);
    private static readonly PackageId Package = Channel.Content.Package;

    // §10.2 step 1: without a Manifest a node wants the listed one and nothing else; once it holds it, the rest follows.
    [Fact]
    public void ANodeWithoutAManifestWantsItFirstAndNothingElseUntilItVerifies()
    {
        VerifiedGrant grant = Samples.Grant(TestKeys.Alice, Channel, TestKeys.Bob, GrantRights.Read);
        var head = new Head(TestKeys.AlicePhone.PublicKey.AsDeviceId(), Hash("e1"), 1);
        var theirs = new WireState(Package, Channel.Hash, 1, [grant.Hash], [head]);
        TrustView trust = AliceKnown();

        IReadOnlyList<PlannedWant> before = ReplicationPlanner.Plan(View(manifest: null), theirs, trust);
        IReadOnlyList<PlannedWant> after = ReplicationPlanner.Plan(View(Channel.Content.Stamp), theirs, trust.With(Channel));

        Assert.Equal(new[] { new PlannedWant(Channel.Hash, WantKind.Manifest) }, before);
        Assert.Equal(new[] { new PlannedWant(grant.Hash, WantKind.Grant), new PlannedWant(head.Entry, WantKind.Head) }, after);
    }

    // §10.2 step 1, a newer Manifest: a higher seq is wanted, an older one is not.
    [Fact]
    public void ANewerListedManifestIsWanted_AnOlderOneIsNot()
    {
        VerifiedManifest newer = Samples.Channel(TestKeys.Alice, seq: 2);
        TrustView trust = AliceKnown().With(Channel);

        IReadOnlyList<PlannedWant> forNewer = ReplicationPlanner.Plan(View(Channel.Content.Stamp), new WireState(Package, newer.Hash, 2, [], []), trust);
        IReadOnlyList<PlannedWant> forOlder = ReplicationPlanner.Plan(View(newer.Content.Stamp), new WireState(Package, Channel.Hash, 1, [], []), trust);

        Assert.Equal(new[] { new PlannedWant(newer.Hash, WantKind.Manifest) }, forNewer);
        Assert.Empty(forOlder);
    }

    // §10.2 step 3: a Head is wanted only when its device is, in the rosters held, a not-removed device of the owner or of
    // a current Write grantee, and its clock is above the Head held for that device.
    [Theory]
    [InlineData("alice-phone", 5, 3, "none", false, true)] // the owner's device, a higher clock: wanted
    [InlineData("alice-phone", 3, 3, "none", false, false)] // the owner's device, the clock held already: not wanted
    [InlineData("alice-phone", 5, -1, "none", false, true)] // the owner's device, no Head held for it yet: wanted
    [InlineData("alice-phone", 5, 3, "none", true, false)] // the owner's device, removed in the roster held: not wanted
    [InlineData("bob-desktop", 1, -1, "write", false, true)] // a device of a Write grantee: wanted
    [InlineData("bob-desktop", 1, -1, "read", false, false)] // a device of a Read grantee: not wanted
    [InlineData("bob-desktop", 1, -1, "none", false, false)] // a device of a user without a Grant: not wanted
    [InlineData("eve-device", 1, -1, "none", false, false)] // a device of no roster held: not wanted
    public void AHeadIsWantedOnlyFromARosterDeviceOfTheOwnerOrAWriteGranteeWithAHigherClock(string device, long theirClock, long heldClock, string bobsRights, bool removed, bool wanted)
    {
        DeviceId writer = TestIdentity.Of(device).DeviceId;
        TrustView trust = new TrustView().With(Channel)
            .With(removed ? TestIdentity.RosterOf("alice", 2, "alice-phone") : TestIdentity.RosterOf("alice"))
            .With(TestIdentity.RosterOf("bob"));
        if (bobsRights != "none")
            trust.With(Samples.Grant(TestKeys.Alice, Channel, TestKeys.Bob, bobsRights == "write" ? GrantRights.Write : GrantRights.Read));
        IReadOnlyList<Head> held = heldClock < 0 ? [] : [new Head(writer, Hash("held"), (ulong)heldClock)];
        var theirs = new WireState(Package, Channel.Hash, 1, [], [new Head(writer, Hash("theirs"), (ulong)theirClock)]);

        IReadOnlyList<PlannedWant> plan = ReplicationPlanner.Plan(View(Channel.Content.Stamp, held), theirs, trust);

        Assert.Equal(wanted, plan.Contains(new PlannedWant(Hash("theirs"), WantKind.Head)));
    }

    // §10.3, §11.2: an Entry's parents always; its payload objects, a Tree's entries and a ChunkList's Chunks as far as the
    // retention row keeps them; under the phone row an Archive keeps only its newest Version's Tree and ChunkLists.
    [Theory]
    [InlineData("everything", "archive", "entry", false, "parent,tree")] // the desktop row: the Version's Tree too
    [InlineData("latest", "archive", "entry", false, "parent")] // the phone row, an older Version: its parents only
    [InlineData("latest", "archive", "entry", true, "parent,tree")] // the phone row, the newest Version: its Tree too
    [InlineData("everything", "archive", "tree", false, "list,tree,link")] // the desktop row: every entry of a Tree
    [InlineData("latest", "archive", "tree", false, "list,tree")] // the phone row: a Tree's ChunkLists and Trees, no symlink Chunk
    [InlineData("everything", "archive", "list", false, "chunk")] // the desktop row: a ChunkList's Chunks
    [InlineData("latest", "archive", "list", false, "")] // the phone row: Chunks on demand only
    [InlineData("latest", "channel", "message", false, "parent")] // the phone row, a Channel: attachments on demand only
    [InlineData("everything", "channel", "message", false, "parent,tree")] // the desktop row, a Channel: every attachment
    public void ParentsAreWantedUntilAHeldEntry_ThenPayloadObjectsAsFarAsThePolicyGoes(string row, string kind, string stored, bool newest, string expected)
    {
        Chunk link = Samples.Chunk(7);
        Chunk data = Samples.Chunk(8);
        ChunkList list = ObjectBuilders.ChunkList((ulong)data.Data.Length, [data.Hash]);
        Tree sub = Samples.Tree("inner.txt", list);
        Assert.True(ObjectBuilders.TryTree(
            [new TreeEntry("file", TreeEntryMode.File, list.Hash, list.Size), new TreeEntry("folder", TreeEntryMode.Directory, sub.Hash, 0), new TreeEntry("link", TreeEntryMode.Symlink, link.Hash, (ulong)link.Data.Length)],
            out Tree? tree,
            out string? problem), problem);
        ObjectHash parent = Hash("parent");
        (ObjectTag tag, ObjectLinks links) = stored switch
        {
            "entry" => (ObjectTag.Entry, Links(Samples.Version(TestKeys.AliceLaptop, Package, 2, EntryParents.Of(parent), tree.Hash), newest)),
            "message" => (ObjectTag.Entry, Links(Samples.Message(TestKeys.AliceLaptop, Package, 2, EntryParents.Of(parent), "photo", new Attachment("a.jpg", tree.Hash, 0)), false)),
            "tree" => (ObjectTag.Tree, ObjectLinks.Of(tree, false)),
            _ => (ObjectTag.ChunkList, ObjectLinks.Of(list, false)),
        };

        IReadOnlyList<PlannedWant> walk = ReplicationPlanner.Walk(tag, links, row == "everything" ? RetentionRow.Everything : RetentionRow.LatestOnly, kind == "archive" ? PackageKind.Archive : PackageKind.Channel);

        var names = new Dictionary<ObjectHash, string> { [parent] = "parent", [tree.Hash] = "tree", [list.Hash] = "list", [sub.Hash] = "tree", [link.Hash] = "link", [data.Hash] = "chunk" };
        Assert.Equal(expected, string.Join(',', walk.Select(w => names[w.Hash])));
    }

    // §10.3 end to end: Bob holds the first Entry; after two more he wants the newest from the Head, its parent, and stops
    // at the Entry he holds: Alice is never asked for it again.
    [Fact]
    public async Task TheWalkStopsAtTheFirstEntryHeld()
    {
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        PackageId channel = await alice.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await alice.GrantAsync(channel, "bob", GrantRights.Read, None);
        await bob.FollowAsync(channel, None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        ObjectHash first = await alice.WriteEntryAsync(channel, ObjectFixtures.Message("one"), None);
        await bob.WaitForEntryAsync(first, None);
        await mesh.SettleAsync(None);
        mesh.Partition("alice-laptop");
        IReadOnlyList<ObjectHash> later = await ObjectFixtures.WriteMessagesAsync(alice, channel, 2, None);
        long since = alice.Node.Facts.LastSequence;

        mesh.Heal();
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await bob.WaitForEntryAsync(later[1], None);
        await bob.WaitForEntryAsync(later[0], None);
        await mesh.SettleAsync(None);

        ObjectHash[] answered = [.. alice.Node.Facts.Recent().Where(f => f.Sequence > since && f.Kind == NodeFactKind.WantAnswered).Select(f => new ObjectHash(f.Subject))];
        Assert.Contains(later[0], answered);
        Assert.Contains(later[1], answered);
        Assert.DoesNotContain(first, answered);
    }

    private static ObjectLinks Links(VerifiedEntry entry, bool newest) => ObjectLinks.Of(Samples.Admit(entry, AliceKnown().With(Channel)), newest);

    private static TrustView AliceKnown() => new TrustView().With(TestIdentity.RosterOf("alice"));

    private static PackageView View(ObjectSeq? manifest, IReadOnlyList<Head>? heads = null) =>
        new(Package, PackageState.Followed, manifest, Channel.Content.Owner, new HashSet<ObjectHash>(), heads ?? []);

    private static ObjectHash Hash(string name) => new(Hash32.Of(System.Text.Encoding.UTF8.GetBytes(name)));
}
