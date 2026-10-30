using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §6.4, history and objects: <c>ListHistory</c> walks the total order of SPEC.md §9.3, either way, after
/// a cursor, with kinds and a limit of 1 to 64; <c>ListStored</c> walks store sequences from <c>fromSeq</c> (0 reads as
/// 1); <c>GetEntry</c> and <c>GetObject</c> return only what is stored; a honoured Redaction marks its target redacted.
/// </summary>
public sealed class HistoryRequestTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // SPEC.md §9.3: Entries of two devices with equal clocks come in (clock, deviceId, hash) order, a page at a time
    // after the cursor, and in exactly the reverse order with reverse set.
    [Fact]
    public async Task HistoryPagesInTheTotalOrder_AfterTheCursor_AndReversed()
    {
        (TestMesh mesh, PackageId channel, List<EntryKey> order) = await TwoDeviceHistoryAsync();
        await using TestMesh owned = mesh;
        LocalClient client = await mesh["alice-laptop"].ConnectUserAsync("app", None);

        var forward = new List<EntryKey>();
        EntryKey? cursor = null;
        bool more = true;
        while (more)
        {
            EntryPage page = (await client.SendAsync(new ListHistory { PackageId = channel, After = cursor, Limit = 2 }, None)).Value;
            Assert.True(page.Entries.Count <= 2);
            forward.AddRange(page.Entries.Select(KeyOf));
            cursor = forward[^1];
            more = page.More;
        }

        EntryPage reversed = (await client.SendAsync(new ListHistory { PackageId = channel, Reverse = true, Limit = 64 }, None)).Value;
        EntryPage lastTwo = (await client.SendAsync(new ListHistory { PackageId = channel, Reverse = true, Limit = 2 }, None)).Value;
        EntryPage beforeThem = (await client.SendAsync(new ListHistory { PackageId = channel, Reverse = true, After = KeyOf(lastTwo.Entries[^1]), Limit = 64 }, None)).Value;

        Assert.Equal(order, forward);
        Assert.Equal(Enumerable.Reverse(order), reversed.Entries.Select(KeyOf));
        Assert.False(reversed.More);
        Assert.True(lastTwo.More);
        Assert.Equal(Enumerable.Reverse(order).Skip(2), beforeThem.Entries.Select(KeyOf));
    }

    // §6.4: a limit of 0 or 65 is Invalid, kinds not ascending are Invalid, an unknown Package is NotFound; the kinds
    // filter keeps only Entries of those kinds.
    [Fact]
    public async Task ALimitOutside1To64IsInvalid_AnUnknownPackageIsNotFound_KindsFilter()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode laptop = mesh["alice-laptop"];
        LocalClient client = await laptop.ConnectUserAsync("app", None);
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        ObjectHash message = await laptop.WriteEntryAsync(channel, ObjectFixtures.Message("hello"), None);
        ObjectHash redaction = await laptop.WriteEntryAsync(channel, new RedactionPayload { Target = message }, None);

        Assert.Equal(Status.Invalid, (await client.SendAsync(new ListHistory { PackageId = channel, Limit = 0 }, None)).Status);
        Assert.Equal(Status.Invalid, (await client.SendAsync(new ListHistory { PackageId = channel, Limit = 65 }, None)).Status);
        Assert.Equal(Status.Invalid, (await client.SendAsync(new ListHistory { PackageId = channel, Limit = 5, Kinds = [EntryKind.Redaction, EntryKind.Message] }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new ListHistory { PackageId = PackageId.Derive(laptop.User, default), Limit = 5 }, None)).Status);
        Assert.Equal([redaction], (await client.SendAsync(new ListHistory { PackageId = channel, Limit = 5, Kinds = [EntryKind.Redaction] }, None)).Value.Entries.Select(e => e.Hash));
    }

    // §5.6, SPEC.md §9.4: a Redaction by the target's own device marks the target redacted; its bytes stay.
    [Fact]
    public async Task ARedactionByTheTargetsDeviceMarksTheTargetRedacted()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode laptop = mesh["alice-laptop"];
        LocalClient client = await laptop.ConnectUserAsync("app", None);
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        ObjectHash message = await laptop.WriteEntryAsync(channel, ObjectFixtures.Message("oops"), None);
        Assert.False((await client.SendAsync(new GetEntry { Hash = message }, None)).Value.Redacted);

        await laptop.WriteEntryAsync(channel, new RedactionPayload { Target = message }, None);

        Entry redacted = (await client.SendAsync(new GetEntry { Hash = message }, None)).Value;
        Assert.True(redacted.Redacted);
        Assert.Equal(message, redacted.Hash);
        Assert.True((await client.SendAsync(new ListHistory { PackageId = channel, Limit = 1 }, None)).Value.Entries[0].Redacted);
    }

    // §6.4: GetEntry returns a stored Entry with its store sequence; GetObject any stored object, a Roster included; what
    // is not stored is NotFound for both.
    [Fact]
    public async Task GetEntryAndGetObjectReturnOnlyWhatIsStored()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode laptop = mesh["alice-laptop"];
        LocalClient client = await laptop.ConnectUserAsync("app", None);
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        ObjectHash message = await laptop.WriteEntryAsync(channel, ObjectFixtures.Message("hello"), None);
        VerifiedRoster roster = TestIdentity.RosterOf("alice");

        Entry entry = (await client.SendAsync(new GetEntry { Hash = message }, None)).Value;
        System.ReadOnlyMemory<byte> bytes = (await client.SendAsync(new GetObject { Hash = roster.Hash }, None)).Value;

        Assert.Equal((message, channel, laptop.Device, 0ul, EntryKind.Message, 1ul), (entry.Hash, entry.PackageId, entry.DeviceId, entry.Clock, entry.Kind, entry.StoreSeq));
        Assert.Equal(roster.Hash.Value, Hash32.Of(bytes.Span));
        Assert.Equal(Status.NotFound, (await client.SendAsync(new GetEntry { Hash = roster.Hash }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new GetObject { Hash = new ObjectHash(Hash32.Of([1])) }, None)).Status);
    }

    // §6.4: ListStored walks store sequences from fromSeq (0 reads as 1) across Packages or within one, a page at a time.
    [Fact]
    public async Task ListStoredWalksStoreSequencesFromFromSeq_AcrossPackagesOrWithinOne()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode laptop = mesh["alice-laptop"];
        LocalClient client = await laptop.ConnectUserAsync("app", None);
        PackageId one = await laptop.CreatePackageAsync("One", PackageKind.Channel, Retention.Default, None);
        PackageId two = await laptop.CreatePackageAsync("Two", PackageKind.Channel, Retention.Default, None);
        foreach (PackageId package in new[] { one, two, one, two, one })
            await laptop.WriteEntryAsync(package, ObjectFixtures.Message("m"), None);

        EntryPage all = (await client.SendAsync(new ListStored { FromSeq = 0, Limit = 64 }, None)).Value;
        EntryPage page = (await client.SendAsync(new ListStored { FromSeq = 2, Limit = 2 }, None)).Value;
        EntryPage ofTwo = (await client.SendAsync(new ListStored { PackageId = two, Limit = 64 }, None)).Value;

        Assert.Equal([1ul, 2, 3, 4, 5], all.Entries.Select(e => e.StoreSeq));
        Assert.False(all.More);
        Assert.Equal([2ul, 3], page.Entries.Select(e => e.StoreSeq));
        Assert.True(page.More);
        Assert.Equal([2ul, 4], ofTwo.Entries.Select(e => e.StoreSeq));
        Assert.Equal(Status.Invalid, (await client.SendAsync(new ListStored { Limit = 0 }, None)).Status);
    }

    private static EntryKey KeyOf(Entry entry) => new(entry.Clock, entry.DeviceId, entry.Hash);

    // Alice's laptop writes four Messages; her phone's Entries, admitted at the laptop, share clocks 1 and 2 with them.
    private static async Task<(TestMesh Mesh, PackageId Channel, List<EntryKey> Order)> TwoDeviceHistoryAsync()
    {
        TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode laptop = mesh["alice-laptop"];
        PackageId channel = await laptop.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        IReadOnlyList<ObjectHash> written = await ObjectFixtures.WriteMessagesAsync(laptop, channel, 4, None);
        var phone = new List<VerifiedEntry>
        {
            Samples.Message(TestKeys.AlicePhone, channel, 1, EntryParents.Of(written[0]), "phone one"),
            Samples.Message(TestKeys.AlicePhone, channel, 2, EntryParents.Of(written[1]), "phone two"),
        };
        foreach (VerifiedEntry entry in phone)
            Assert.True(await laptop.Node.PutAsync(Samples.Admit(entry, laptop.Node.Trust), None));

        // SPEC.md §9.3: one device writing alone takes clocks 0, 1, 2, 3.
        List<EntryKey> keys = [.. written.Select((hash, clock) => new EntryKey((ulong)clock, laptop.Device, hash))];
        keys.AddRange(phone.Select(p => p.Content.Key));
        return (mesh, channel, [.. keys.Order()]);
    }
}
