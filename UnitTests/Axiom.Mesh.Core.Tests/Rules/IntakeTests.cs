using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>SPEC.md §10.6: the order of verification, the held buffer and its bounds; §7.6 and §12.2 for what is refused.</summary>
public sealed class IntakeTests
{
    private const int NearlyAMebibyte = 1_000_000;

    /// <summary>Every order in which Bob's Roster, the Manifest, the Grant, Bob's Entry and its attachment can arrive.</summary>
    public static TheoryData<string> Arrivals()
    {
        var orders = new TheoryData<string>();
        foreach (IEnumerable<char> order in Permutations("RMGEX"))
            orders.Add(new string([.. order]));
        return orders;
    }

    // SPEC.md §10.6: Roster (needs its user known) → Manifest (needs the Package followed or listed) → Grant (needs the
    // Manifest) → Entry (needs the writer's Roster and Grant) → payload (needs its hash). Bob is known only through the
    // Grant, so his Roster waits for it, and his Entry for both; the attachment needs nothing but to be wanted.
    [Theory]
    [MemberData(nameof(Arrivals))]
    public async Task ObjectsApplyInTheOrderRosterManifestGrantEntryPayloadWhateverTheirArrival_Bounded8MiBPerSession64MiBInAll(string arrival)
    {
        await using TestStore node = await TestStore.StartAsync();
        await node.IdentifyAsync(TestKeys.Alice, TestKeys.AliceLaptop);
        VerifiedManifest manifest = Samples.Channel(TestKeys.Alice);
        PackageId channel = manifest.Content.Package;
        await node.FollowAsync(channel);
        Chunk attachment = Samples.Chunk(1);
        node.Wanted.Want(attachment.Hash);
        var objects = new Dictionary<char, (ObjectHash Hash, ReadOnlyMemory<byte> Bytes)>
        {
            ['R'] = Of(Samples.Roster(TestKeys.Bob, 1, (TestKeys.BobDesktop, false))),
            ['M'] = Of(manifest),
            ['G'] = Of(Samples.Grant(TestKeys.Alice, manifest, TestKeys.Bob, GrantRights.Write)),
            ['E'] = Of(Samples.Message(TestKeys.BobDesktop, channel, 0, EntryParents.None, "hi", new Attachment("a.txt", attachment.Hash, 1000))),
            ['X'] = (attachment.Hash, attachment.Bytes),
        };
        using ChangeSubscription changes = node.Bus.Subscribe(64);

        foreach (char letter in arrival)
            await node.ReceiveAsync(objects[letter].Bytes, objects[letter].Hash, TestKeys.AlicePhone);
        await node.Writer.FlushAsync(default);

        var stored = new List<ObjectHash>();
        while (changes.Reader.TryRead(out ChangeSet? set))
            stored.AddRange(set.Stored.Select(s => s.Hash));
        int At(char letter) => stored.IndexOf(objects[letter].Hash);
        Assert.All("RMGEX", letter => Assert.True(At(letter) >= 0, $"{letter} was not stored."));
        Assert.True(At('M') < At('G') && At('G') < At('R') && At('R') < At('E'), "Stored as " + string.Join(",", "RMGEX".OrderBy(At)));
        Assert.Equal(0, node.Intake.Held.Count);
        Assert.Contains((PinReason.Policy, channel.Value), await node.PinsOfAsync(attachment.Hash));
    }

    // SPEC.md §10.6: at most 8 MiB per session and 64 MiB in all, oldest dropped beyond that; dropped 10 minutes after the
    // sender disconnected.
    [Fact]
    public async Task TheHeldBufferKeeps8MiBPerSessionAnd64MiBInAll_OldestFirst_AndDropsWhatWaits10MinutesAfterItsSenderLeft()
    {
        await using TestStore node = await TestStore.StartAsync();
        PackageId unknown = Samples.Channel(TestKeys.Eve).Content.Package;
        var first = new List<VerifiedEntry>();
        for (byte i = 0; i < 9; i++)
            first.Add(Samples.Opaque(TestKeys.EveDevice, unknown, i, NearlyAMebibyte, i));
        foreach (VerifiedEntry entry in first)
            Assert.Equal(IntakeVerdict.Held, (await node.ReceiveAsync(entry.Bytes, entry.Hash, TestKeys.EveDevice)).Verdict);

        var eve = Origin.FromPeer(TestKeys.EveDevice.PublicKey.AsDeviceId());
        Assert.InRange(node.Intake.Held.BytesOf(eve), 7 * NearlyAMebibyte, Limits.MaxHeldBytesPerSession);
        Assert.False(node.Intake.Held.Contains(first[0].Hash), "The oldest of the session was kept past 8 MiB.");
        Assert.True(node.Intake.Held.Contains(first[^1].Hash));

        for (int sender = 1; sender <= 9; sender++)
        {
            for (int i = 0; i < 8; i++)
            {
                VerifiedEntry entry = Samples.Opaque(TestKeys.EveDevice, unknown, (ulong)(100 * sender) + (ulong)i, NearlyAMebibyte, (byte)(sender * 16 + i));
                await node.Intake.ReceiveAsync(entry.Hash, entry.Bytes, Origin.FromPeer(new DeviceId(Hash32.Of([(byte)sender]))), null, node.Now, default);
            }
        }

        Assert.InRange(node.Intake.Held.Bytes, Limits.MaxHeldBytes - (2 * NearlyAMebibyte), Limits.MaxHeldBytes);
        Assert.Equal(0, node.Intake.Held.BytesOf(eve)); // the oldest overall went first

        var last = Origin.FromPeer(new DeviceId(Hash32.Of([9])));
        long held = node.Intake.Held.BytesOf(last);
        node.Intake.OriginGone(last, node.Now);
        node.Time.Advance(TimeSpan.FromSeconds(Limits.HeldAfterDisconnectSeconds) - TimeSpan.FromMilliseconds(1));
        Assert.Empty(node.Intake.Expire(node.Now));
        node.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(held, node.Intake.Expire(node.Now).Sum(h => h.Bytes));
        Assert.Equal(0, node.Intake.Held.BytesOf(last));
    }

    // SPEC.md §7.6 and §12.1: bytes that are not the object named are a violation. §12.2: a malformed object, a signature
    // that does not verify against a key the node holds, an Entry from a removed device are faulty. §10.3: an unsigned
    // object nobody wanted is dropped. None of them is stored.
    [Fact]
    public async Task BytesThatAreNotTheNamedObjectAreAViolation_MalformedOrBadlySignedContentIsFaulty_AndAnUnwantedChunkIsDropped()
    {
        await using TestStore node = await TestStore.StartAsync();
        await node.IdentifyAsync(TestKeys.Alice, TestKeys.AliceLaptop);
        VerifiedManifest manifest = Samples.Channel(TestKeys.Alice);
        await node.FollowAsync(manifest.Content.Package);
        await node.PutAsync(manifest);
        await node.PutAsync(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, true)));
        Chunk chunk = Samples.Chunk(2);

        IntakeOutcome renamed = await node.ReceiveAsync(chunk.Bytes, Samples.Chunk(3).Hash, TestKeys.BobDesktop);
        byte[] flipped = manifest.Bytes.ToArray();
        flipped[40] ^= 1;
        IntakeOutcome badSignature = await node.ReceiveAsync(flipped, ObjectHash.Of(flipped), TestKeys.BobDesktop);
        byte[] truncated = chunk.Bytes[..1].ToArray();
        IntakeOutcome malformed = await node.ReceiveAsync(truncated, ObjectHash.Of(truncated), TestKeys.BobDesktop);
        VerifiedEntry fromRemoved = Samples.Message(TestKeys.AlicePhone, manifest.Content.Package, 0, EntryParents.None);
        IntakeOutcome removed = await node.ReceiveAsync(fromRemoved.Bytes, fromRemoved.Hash, TestKeys.BobDesktop);
        IntakeOutcome unwanted = await node.ReceiveAsync(chunk, TestKeys.BobDesktop);

        Assert.Equal(IntakeVerdict.Violation, renamed.Verdict);
        Assert.Equal(IntakeVerdict.Faulty, badSignature.Verdict);
        Assert.Equal(IntakeVerdict.Faulty, malformed.Verdict);
        Assert.Equal(IntakeVerdict.Faulty, removed.Verdict);
        Assert.Equal(IntakeVerdict.Dropped, unwanted.Verdict);
        await node.Writer.FlushAsync(default);
        Assert.False(await node.ContainsAsync(chunk.Hash));
        Assert.False(await node.ContainsAsync(fromRemoved.Hash));
        Assert.Equal(0, node.Intake.Held.Count);
    }

    private static (ObjectHash Hash, ReadOnlyMemory<byte> Bytes) Of(StorableObject storable) => (storable.Hash, storable.Bytes);

    private static (ObjectHash Hash, ReadOnlyMemory<byte> Bytes) Of(VerifiedEntry entry) => (entry.Hash, entry.Bytes);

    private static IEnumerable<IEnumerable<char>> Permutations(string letters) =>
        letters.Length <= 1
            ? [letters]
            : letters.SelectMany((c, i) => Permutations(letters.Remove(i, 1)).Select(rest => rest.Prepend(c)));
}
