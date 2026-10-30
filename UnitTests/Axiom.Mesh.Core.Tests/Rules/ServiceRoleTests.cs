using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §9.2 and §9.4 over the local port, with the configuration of <see cref="LocalMeshes"/>: <c>sync</c> in
/// process (Archive, Version), <c>cli</c> external (Archive and Channel; Version, Message, Redaction), <c>worker</c>
/// spawned with no kinds. Registration is bounded by the configuration and by the other services' registrations;
/// <c>WriteEntry</c> chooses parents as §9.4 says and refuses what §9.4 refuses.
/// </summary>
public sealed class ServiceRoleTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §9.2: a kind outside the service's configuration is NotPermitted; a kind another connected service holds is
    // KindTaken; an Entry kind another registered kind lists is Invalid; registering a held kind again is OK; a kind is
    // released when its connection ends.
    [Fact]
    public async Task AKindOutsideTheConfigurationIsNotPermitted_OneAnotherServiceHoldsIsKindTaken()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient sync = await node.ConnectInProcessAsync("sync", None);
        LocalClient cli = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        LocalClient worker = await node.ConnectServiceAsync("worker", LocalMeshes.WorkerToken, None);
        KindRegistration archive = (await sync.SendAsync(new RegisterKind { Kind = PackageKind.Archive, EntryKinds = [EntryKind.Version] }, None)).Value;

        Assert.Equal(Status.KindTaken, (await cli.SendAsync(new RegisterKind { Kind = PackageKind.Archive, EntryKinds = [EntryKind.Version] }, None)).Status);
        Assert.Equal(Status.NotPermitted, (await cli.SendAsync(new RegisterKind { Kind = (PackageKind)7, EntryKinds = [EntryKind.Message] }, None)).Status);
        Assert.Equal(Status.NotPermitted, (await cli.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [(EntryKind)9] }, None)).Status);
        Assert.Equal(Status.NotPermitted, (await worker.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message] }, None)).Status);
        Assert.Equal(Status.Invalid, (await cli.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Version, EntryKind.Message] }, None)).Status);
        Assert.Equal(Status.Invalid, (await cli.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [] }, None)).Status);
        Assert.True((await cli.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message, EntryKind.Redaction] }, None)).IsOk);
        Assert.True((await cli.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message, EntryKind.Redaction] }, None)).IsOk);
        Assert.Equal(sync.Host.StoreEpoch, archive.StoreEpoch);
        Assert.Equal(0u, archive.Packages);

        await sync.ByeAsync(None);
        await Waits.UntilAsync(() => node.Node.Local.Connections == 2, "the in-process service's connection to end");
        Assert.Equal(Status.Invalid, (await cli.SendAsync(new RegisterKind { Kind = PackageKind.Archive, EntryKinds = [EntryKind.Message] }, None)).Status);
        Assert.True((await cli.SendAsync(new RegisterKind { Kind = PackageKind.Archive, EntryKinds = [EntryKind.Version] }, None)).IsOk);
    }

    // §9.4: no Entry stored gives clock 0 and no parents; the own Head is the next parent; with only another device's
    // Entries stored the highest of them is; parents named are kept with the clock one above the highest stored; and when
    // the highest clock is 2^32 + 1 above the own Head, the Entry names both. Each verifies, comes last in the total order
    // and is faultless under SPEC.md §8.6.
    [Fact]
    public async Task WithNoParentsTheEntryVerifiesOrdersAndIsFaultless_ForTheFourParentCases_AndTwoParentsWhenTheHighestClockIs2Pow32Plus1AboveTheOwnHead()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode laptop = mesh[LocalMeshes.Node];
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        PackageId other = await laptop.CreatePackageAsync("Work", PackageKind.Channel, Retention.Default, None);
        LocalClient cli = await laptop.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        Assert.True((await cli.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message, EntryKind.Redaction] }, None)).IsOk);

        Entry first = await WriteAsync(cli, channel, "nothing stored");
        Assert.Equal((0ul, 0), (first.Clock, first.Parents.Count));

        Entry second = await WriteAsync(cli, channel, "after the own Head");
        Assert.Equal(1ul, second.Clock);
        Assert.Equal([first.Hash], second.Parents);

        VerifiedEntry phones = Samples.Message(TestKeys.AlicePhone, other, 0, EntryParents.None, "the phone's");
        Assert.True(await laptop.Node.PutAsync(Samples.Admit(phones, laptop.Node.Trust), None));
        Entry highest = await WriteAsync(cli, other, "no own Head");
        Assert.Equal(1ul, highest.Clock);
        Assert.Equal([phones.Hash], highest.Parents);

        Entry named = await WriteAsync(cli, channel, "named parent", [first.Hash]);
        Assert.Equal(2ul, named.Clock);
        Assert.Equal([first.Hash], named.Parents);

        VerifiedEntry near = Samples.Message(TestKeys.AlicePhone, channel, 3, EntryParents.Of(named.Hash), "near");
        VerifiedEntry far = Samples.Message(TestKeys.AlicePhone, channel, 3 + Limits.MaxClockGap, EntryParents.Of(near.Hash), "far");
        Assert.True(await laptop.Node.PutAsync(Samples.Admit(near, laptop.Node.Trust), None));
        Assert.True(await laptop.Node.PutAsync(Samples.Admit(far, laptop.Node.Trust), None));
        Assert.Equal(named.Clock + Limits.MaxClockGap + 1, far.Content.Clock);
        Entry two = await WriteAsync(cli, channel, "two parents");
        Assert.Equal(far.Content.Clock + 1, two.Clock);
        Assert.Equal(new[] { named.Hash, far.Hash }.Order(), two.Parents);

        foreach (Entry entry in new[] { first, second, highest, named, two })
            await AssertVerifiesAndIsFaultlessAsync(cli, entry);
    }

    // §9.4: a Package not followed is NotFound; an Entry kind not registered is KindNotRegistered; a referenced object
    // not stored is NotFound; references stated for kinds 1 to 3 and a named parent of another Package are Invalid.
    [Fact]
    public async Task WriteEntryRefusesWhatSection9_4Refuses()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode laptop = mesh[LocalMeshes.Node];
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        PackageId other = await laptop.CreatePackageAsync("Work", PackageKind.Channel, Retention.Default, None);
        ObjectHash elsewhere = await laptop.WriteEntryAsync(other, ObjectFixtures.Message("elsewhere"), None);
        LocalClient cli = await laptop.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        byte[] message = PayloadEncoder.Encode(ObjectFixtures.Message("hello"));
        Assert.Equal(Status.KindNotRegistered, (await cli.SendAsync(new WriteEntry { PackageId = channel, Kind = EntryKind.Message, Payload = message }, None)).Status);
        Assert.True((await cli.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message, EntryKind.Redaction] }, None)).IsOk);
        Chunk absent = Samples.Chunk(7);
        byte[] attached = PayloadEncoder.Encode(new MessagePayload { Text = "see attached", Attachments = [new Attachment("a.txt", absent.Hash, 1000)], ReplyTo = default });

        Assert.Equal(Status.NotFound, (await cli.SendAsync(new WriteEntry { PackageId = PackageId.Derive(laptop.User, default), Kind = EntryKind.Message, Payload = message }, None)).Status);
        Assert.Equal(Status.NotFound, (await cli.SendAsync(new WriteEntry { PackageId = channel, Kind = EntryKind.Message, Payload = attached }, None)).Status);
        Assert.Equal(Status.Invalid, (await cli.SendAsync(new WriteEntry { PackageId = channel, Kind = EntryKind.Message, Payload = message, References = [absent.Hash] }, None)).Status);
        Assert.Equal(Status.Invalid, (await cli.SendAsync(new WriteEntry { PackageId = channel, Kind = EntryKind.Message, Payload = message, Parents = [elsewhere] }, None)).Status);
        Assert.Empty((await cli.SendAsync(new ListHistory { PackageId = channel, Limit = 64 }, None)).Value.Entries);
    }

    // §9.4, SPEC.md §8.5: a Version, Message or Redaction payload that does not parse to its end is Invalid here; the
    // same kind of Entry from a peer is malformed and dropped as faulty content, never stored.
    [Fact]
    public async Task AVersionMessageOrRedactionPayloadThatDoesNotParseIsInvalidHere_AndDroppedAsFaultyFromAPeer()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode laptop = mesh[LocalMeshes.Node];
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        PackageId archive = await laptop.CreatePackageAsync("Photos", PackageKind.Archive, Retention.Default, None);
        LocalClient cli = await laptop.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        Assert.True((await cli.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message, EntryKind.Redaction] }, None)).IsOk);
        Assert.True((await cli.SendAsync(new RegisterKind { Kind = PackageKind.Archive, EntryKinds = [EntryKind.Version] }, None)).IsOk);
        byte[] trailing = [.. PayloadEncoder.Encode(ObjectFixtures.Message("hello")), 0];

        Assert.Equal(Status.Invalid, (await cli.SendAsync(new WriteEntry { PackageId = channel, Kind = EntryKind.Message, Payload = trailing }, None)).Status);
        Assert.Equal(Status.Invalid, (await cli.SendAsync(new WriteEntry { PackageId = channel, Kind = EntryKind.Redaction, Payload = new byte[31] }, None)).Status);
        Assert.Equal(Status.Invalid, (await cli.SendAsync(new WriteEntry { PackageId = archive, Kind = EntryKind.Version, Payload = new byte[5] }, None)).Status);

        byte[] malformed = Samples.RawEntry(TestKeys.AlicePhone, channel, 0, EntryParents.None, EntryKind.Message, trailing);
        ObjectHash hash = ObjectHash.Of(malformed);
        long before = laptop.Node.Facts.LastSequence;
        await using ScriptedPeer phone = await ScriptedPeer.ConnectAsync(laptop, TestIdentity.Of("alice-phone"), ScriptedPeerOptions.Default, None);
        await phone.SendAsync(phone.Hello(1), None);
        await phone.ExpectAsync<Axiom.Mesh.Protocol.Hello>(None);
        await phone.ExpectAsync<Axiom.Mesh.Protocol.Packages>(None);
        Assert.True(laptop.Node.Trust.TryGetManifest(channel, out VerifiedManifest? manifest));
        await phone.SendAsync(new Axiom.Mesh.Protocol.PackageChanged(new Axiom.Mesh.Protocol.PackageState(channel, manifest.Hash, manifest.Content.Seq, [], [new Head(TestIdentity.Of("alice-phone").DeviceId, hash, 0)])), None);
        Assert.Equal([hash], (await phone.ExpectAsync<Axiom.Mesh.Protocol.Want>(None)).Hashes);
        await phone.SendAsync(Axiom.Mesh.Protocol.ObjectMessage.FromStored(hash, malformed), None);

        NodeFact faulty = await laptop.Node.Facts.WaitForAsync(f => f.Kind == NodeFactKind.FaultyContent, before, None).WaitAsync(TestMesh.Deadline);
        Assert.Equal(hash.Value, faulty.Subject);
        Assert.False(await laptop.Node.ContainsAsync(hash, None));
    }

    private static async Task<Entry> WriteAsync(LocalClient cli, PackageId package, string text, IReadOnlyList<ObjectHash>? parents = null)
    {
        // SPEC.md §9.3: what is written comes last in the Package's total order.
        LocalResult<Entry> written = await cli.SendAsync(
            new WriteEntry { PackageId = package, Kind = EntryKind.Message, Payload = PayloadEncoder.Encode(ObjectFixtures.Message(text)), Parents = parents ?? [] },
            None);
        Assert.True(written.IsOk, $"{written.Status}: {written.Error?.Message}");
        EntryPage last = (await cli.SendAsync(new ListHistory { PackageId = package, Reverse = true, Limit = 1 }, None)).Value;
        Assert.Equal(written.Value.Hash, last.Entries[0].Hash);
        return written.Value;
    }

    // SPEC.md §8.6: the stored bytes verify against the device key, and with its parents held the Entry is not faulty.
    private static async Task AssertVerifiesAndIsFaultlessAsync(LocalClient cli, Entry entry)
    {
        System.ReadOnlyMemory<byte> bytes = (await cli.SendAsync(new GetObject { Hash = entry.Hash }, None)).Value;
        Assert.True(HashedObject.TryVerify(bytes, entry.Hash, out HashedObject hashed));
        Assert.True(Objects.TryParse(hashed, out ParsedObject? parsed, out _));
        Assert.True(VerifiedEntry.TryVerify((UnverifiedEntry)parsed, TestKeys.AliceLaptop.PublicKey, out VerifiedEntry? verified));
        var parents = new List<EntryFacts>();
        foreach (ObjectHash parent in entry.Parents)
        {
            Entry held = (await cli.SendAsync(new GetEntry { Hash = parent }, None)).Value;
            parents.Add(new EntryFacts(held.PackageId, held.Kind, new EntryKey(held.Clock, held.DeviceId, held.Hash)));
        }

        Assert.False(Lamport.TryFindParentFault(verified.Content, parents, out ObjectFault fault), fault.ToString());
        Assert.True(parents.All(p => p.Key < verified.Content.Key));
    }
}
