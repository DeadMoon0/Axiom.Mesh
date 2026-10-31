using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>SPEC.md §11.2 (the retention table per device class) and LOCAL-SPEC.md §14.14 (the local policy).</summary>
public sealed class RetentionTests
{
    // LOCAL-SPEC.md §14.14: the local policy when it is not 0, else the Manifest's retention when it is not 0, else the
    // device class's default (SPEC.md §11.2: desktop keeps everything, phone the latest). Columns: the device class (0
    // desktop, 1 phone), the local policy and the Manifest's retention (0 default, 1 everything, 2 latest only), and the
    // row expected (0 everything, 1 latest only).
    [Theory]
    [InlineData(0, 0, 0, 0)] // desktop default
    [InlineData(1, 0, 0, 1)] // phone default
    [InlineData(1, 0, 1, 0)] // the owner's hint over the class
    [InlineData(0, 0, 2, 1)] // the hint on a desktop
    [InlineData(0, 2, 1, 1)] // the local policy over the hint
    [InlineData(1, 1, 2, 0)] // the local policy over both
    public void TheRowIsTheLocalPolicyElseTheManifestsHintElseTheDeviceClassDefault(int deviceClass, int policy, int hint, int expected) =>
        Assert.Equal((RetentionRow)expected, RetentionRule.Effective((DeviceClass)deviceClass, (LocalRetention)policy, (Retention)hint));

    // SPEC.md §11.2: a desktop keeps every Version, Tree, ChunkList and Chunk, every Message and attachment; a phone every
    // Version, the newest Version's Tree and ChunkLists, Chunks and attachments on demand (not pinned).
    [Fact]
    public async Task ADesktopKeepsEverything_APhoneTheNewestVersionsTreeAndChunkListsButNoChunksOrAttachments()
    {
        foreach (DeviceClass deviceClass in new[] { DeviceClass.Desktop, DeviceClass.Phone })
        {
            await using TestStore node = await TestStore.StartAsync(StoreOptions.Default with { DeviceClass = deviceClass });
            await node.IdentifyAsync(TestKeys.Alice, TestKeys.AliceLaptop);
            await node.PutAsync(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, false)));
            VerifiedManifest archive = Samples.Archive(TestKeys.Alice);
            VerifiedManifest channel = Samples.Channel(TestKeys.Alice);
            foreach (VerifiedManifest manifest in new[] { archive, channel })
            {
                await node.FollowAsync(manifest.Content.Package);
                await node.PutAsync(manifest);
            }

            (Tree oldTree, ChunkList oldList, Chunk oldChunk) = await PutFileAsync(node, 1);
            (Tree newTree, ChunkList newList, Chunk newChunk) = await PutFileAsync(node, 2);
            Chunk attachment = Samples.Chunk(3);
            await node.PutAsync(attachment);
            PackageId archiveId = archive.Content.Package;
            VerifiedEntry older = Samples.Version(TestKeys.AlicePhone, archiveId, 0, EntryParents.None, oldTree.Hash);
            await node.PutFromPeerAsync(Samples.Admit(older, node.Trust), TestKeys.AlicePhone);
            await node.PutFromPeerAsync(Samples.Admit(Samples.Version(TestKeys.AlicePhone, archiveId, 1, EntryParents.Of(older.Hash), newTree.Hash), node.Trust), TestKeys.AlicePhone);
            await node.PutFromPeerAsync(Samples.Admit(Samples.Message(TestKeys.AlicePhone, channel.Content.Package, 0, EntryParents.None, "see", new Attachment("a", attachment.Hash, 1000)), node.Trust), TestKeys.AlicePhone);

            bool desktop = deviceClass == DeviceClass.Desktop;
            Assert.True(await KeptAsync(node, newTree));
            Assert.True(await KeptAsync(node, newList));
            Assert.Equal(desktop, await KeptAsync(node, newChunk));
            Assert.Equal(desktop, await KeptAsync(node, oldTree));
            Assert.Equal(desktop, await KeptAsync(node, oldList));
            Assert.Equal(desktop, await KeptAsync(node, oldChunk));
            Assert.Equal(desktop, await KeptAsync(node, attachment));
        }
    }

    private static async Task<(Tree Tree, ChunkList List, Chunk Chunk)> PutFileAsync(TestStore node, byte fill)
    {
        Chunk chunk = Samples.Chunk(fill);
        ChunkList list = ObjectBuilders.ChunkList(1000, [chunk.Hash]);
        Tree tree = Samples.Tree("file-" + fill, list);
        foreach (StorableObject storable in new StorableObject[] { tree, list, chunk })
            await node.PutAsync(storable);
        return (tree, list, chunk);
    }

    private static async Task<bool> KeptAsync(TestStore node, StorableObject storable) =>
        (await node.PinsOfAsync(storable.Hash)).Any(p => p.Reason == PinReason.Policy);
}
