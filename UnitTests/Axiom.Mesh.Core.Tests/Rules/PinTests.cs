using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>SPEC.md §11.2 (pin reasons, Outbound, the history pinned Head) and LOCAL-SPEC.md §6.5 (who may hold which pin).</summary>
public sealed class PinTests
{
    // SPEC.md §11.2, LOCAL-SPEC.md §9.4: every object an Entry written on this device references, through Trees and
    // ChunkLists, is pinned Outbound with the Package as source, whatever the policy keeps; the pin goes per object once
    // it was sent to a peer. An Entry another device wrote pins nothing Outbound here.
    [Fact]
    public async Task EveryObjectAnEntryWrittenHereReferencesIsPinnedOutboundUntilSentOnce()
    {
        await using TestStore node = await TestStore.StartAsync(StoreOptions.Default with { DeviceClass = DeviceClass.Phone });
        await node.IdentifyAsync(TestKeys.Alice, TestKeys.AliceLaptop);
        await node.PutAsync(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, false)));
        VerifiedManifest archive = Samples.Archive(TestKeys.Alice);
        PackageId id = archive.Content.Package;
        await node.FollowAsync(id);
        await node.PutAsync(archive);

        Chunk first = Samples.Chunk(1);
        Chunk second = Samples.Chunk(2, 10);
        ChunkList file = ObjectBuilders.ChunkList(1010, [first.Hash, second.Hash]);
        Tree tree = Samples.Folder("docs", Samples.Tree("a.txt", file));
        Tree inner = Samples.Tree("a.txt", file);
        foreach (StorableObject storable in new StorableObject[] { first, second, file, inner, tree })
            await node.PutAsync(storable);
        await node.WriteAsync(Samples.Version(TestKeys.AliceLaptop, id, 0, EntryParents.None, tree.Hash));

        foreach (StorableObject storable in new StorableObject[] { first, second, file, inner, tree })
            Assert.Contains((PinReason.Outbound, id.Value), await node.PinsOfAsync(storable.Hash));

        Assert.Equal(1, await node.PinsAsync(PinCommandKind.Sent, PinReason.Outbound, default, first.Hash));
        Assert.DoesNotContain((PinReason.Outbound, id.Value), await node.PinsOfAsync(first.Hash));
        Assert.Empty(await node.PinsOfAsync(first.Hash)); // a phone keeps Chunks on demand: nothing else holds it
        Assert.Contains((PinReason.Outbound, id.Value), await node.PinsOfAsync(second.Hash));
        Assert.Equal(0, await node.PinsAsync(PinCommandKind.Sent, PinReason.Outbound, default, first.Hash));

        Chunk fromPhone = Samples.Chunk(3);
        await node.PutAsync(fromPhone);
        await node.PutFromPeerAsync(Samples.Admit(Samples.Message(TestKeys.AlicePhone, id, 0, EntryParents.None, "x", new Attachment("b", fromPhone.Hash, 1000)), node.Trust), TestKeys.AlicePhone);
        Assert.DoesNotContain((PinReason.Outbound, id.Value), await node.PinsOfAsync(fromPhone.Hash));
    }

    // SPEC.md §8.6, §11.2 (draft 5): the Outbound pins come from the envelope's references, of any kind; an object only the
    // payload names is not pinned, because the node never reads a payload.
    [Fact]
    public async Task WhatTheEnvelopeReferencesIsPinnedOutbound_WhatOnlyThePayloadNamesIsNot()
    {
        await using TestStore node = await TestStore.StartAsync(StoreOptions.Default with { DeviceClass = DeviceClass.Phone });
        await node.IdentifyAsync(TestKeys.Alice, TestKeys.AliceLaptop);
        await node.PutAsync(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false)));
        VerifiedManifest channel = Samples.Channel(TestKeys.Alice);
        PackageId id = channel.Content.Package;
        await node.FollowAsync(id);
        await node.PutAsync(channel);

        Chunk referenced = Samples.Chunk(4);
        Chunk named = Samples.Chunk(5);
        await node.PutAsync(referenced);
        await node.PutAsync(named);
        byte[] payload = new byte[Hash32.Length];
        named.Hash.Value.CopyTo(payload);
        await node.WriteAsync(Samples.Written(TestKeys.AliceLaptop, id, 0, EntryParents.None, new EntryBody((EntryKind)500, payload, EntryReferences.Of([referenced.Hash]), default)));

        Assert.Contains((PinReason.Outbound, id.Value), await node.PinsOfAsync(referenced.Hash));
        Assert.Empty(await node.PinsOfAsync(named.Hash));
    }

    // SPEC.md §11.2: the Manifest, the Rosters of involved users, the Grants and every Entry of a followed Package carry
    // reason Head on every device class (source the Package, a Roster's its user); the Manifest of a listed Package
    // carries no pin. Unfollowing releases the Package's Head and Policy pins.
    [Fact]
    public async Task TheHistoryOfAFollowedPackageIsPinnedHead_AListedManifestIsNot_AndUnfollowingReleasesIt()
    {
        await using TestStore node = await TestStore.StartAsync(StoreOptions.Default with { DeviceClass = DeviceClass.Phone });
        await node.IdentifyAsync(TestKeys.Bob, TestKeys.BobDesktop);
        await node.PairAsync(TestKeys.Alice);
        VerifiedRoster alice = Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false));
        await node.PutAsync(alice);
        VerifiedManifest channel = Samples.Channel(TestKeys.Alice);
        PackageId id = channel.Content.Package;
        await node.ListAsync(id);
        await node.PutFromPeerAsync(channel, TestKeys.AliceLaptop);
        Assert.Empty(await node.PinsOfAsync(channel.Hash));

        await node.FollowAsync(id);
        VerifiedGrant grant = Samples.Grant(TestKeys.Alice, channel, TestKeys.Bob, GrantRights.Read);
        await node.PutFromPeerAsync(grant, TestKeys.AliceLaptop);
        VerifiedEntry entry = Samples.Message(TestKeys.AliceLaptop, id, 0, EntryParents.None);
        await node.PutFromPeerAsync(Samples.Admit(entry, node.Trust), TestKeys.AliceLaptop);

        Assert.Equal(new[] { (PinReason.Head, TestKeys.Alice.PublicKey.AsUserId().Value) }, await node.PinsOfAsync(alice.Hash));
        foreach (ObjectHash pinned in new[] { channel.Hash, grant.Hash, entry.Hash })
            Assert.Equal(new[] { (PinReason.Head, id.Value) }, await node.PinsOfAsync(pinned));

        Assert.True(await node.UnfollowAsync(id));
        foreach (ObjectHash released in new[] { channel.Hash, grant.Hash, entry.Hash })
            Assert.Empty(await node.PinsOfAsync(released));
        Assert.False(node.Trust.IsFollowed(id));
        Assert.True(node.Trust.IsFollowedOrListed(id));
    }

    // LOCAL-SPEC.md §6.5: a client pins with reason User or Service only; Pin is idempotent and refuses nothing for a
    // missing object but pins nothing; Unpin touches only its own reason and source; ReleasePins removes every pin of one
    // reason and source.
    [Fact]
    public async Task AClientPinsWithUserOrServiceOnly_UnpinTouchesItsOwnPins_AndReleaseRemovesEveryPinOfASource()
    {
        await using TestStore node = await TestStore.StartAsync();
        Chunk a = Samples.Chunk(1);
        Chunk b = Samples.Chunk(2);
        await node.PutAsync(a);
        await node.PutAsync(b);
        Hash32 service = Hash32.Of("sync"u8);

        Assert.Throws<ArgumentException>(() => new PinCommand(PinCommandKind.Pin, [a.Hash], PinReason.Head, default));
        Assert.Equal(2, await node.PinsAsync(PinCommandKind.Pin, PinReason.Service, service, a.Hash, b.Hash));
        Assert.Equal(0, await node.PinsAsync(PinCommandKind.Pin, PinReason.Service, service, a.Hash));
        Assert.Equal(1, await node.PinsAsync(PinCommandKind.Pin, PinReason.User, default, a.Hash));
        Assert.Equal(0, await node.PinsAsync(PinCommandKind.Pin, PinReason.User, default, Samples.Chunk(3).Hash));

        Assert.Equal(1, await node.PinsAsync(PinCommandKind.Unpin, PinReason.User, default, a.Hash, b.Hash));
        Assert.Equal(2, await node.PinsAsync(PinCommandKind.Release, PinReason.Service, service));
        Assert.Empty(await node.PinsOfAsync(a.Hash));
        Assert.Equal(0, node.Writer.Totals.PinnedBytes);
    }
}
