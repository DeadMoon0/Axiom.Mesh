using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;
using LocalEntry = Axiom.Mesh.Local.Entry;
using LocalPackage = Axiom.Mesh.Local.Package;
using LocalPackageState = Axiom.Mesh.Local.PackageState;
using WireBye = Axiom.Mesh.Protocol.Bye;
using WireByeReason = Axiom.Mesh.Protocol.ByeReason;
using WireChanged = Axiom.Mesh.Protocol.PackageChanged;
using WireHello = Axiom.Mesh.Protocol.Hello;
using WireObject = Axiom.Mesh.Protocol.ObjectMessage;
using WirePackages = Axiom.Mesh.Protocol.Packages;
using WireState = Axiom.Mesh.Protocol.PackageState;
using WireWant = Axiom.Mesh.Protocol.Want;

namespace Axiom.Mesh.Testing.Tests.Rules;

/// <summary>
/// The scenario suite of the fundamentals plan §5: what two, three or four devices of Alice, Bob and Eve end up holding
/// and refusing after pairing, writing, following, going offline, revoking, removing, misbehaving and restarting. Every
/// scenario is written once, against <see cref="IMeshFixture"/>, and runs unchanged in memory on one fake clock
/// (<c>InMemoryScenarios</c>) and on real loopback sockets with real TLS, a store and an index on disk and the real pipe
/// (<c>SocketScenarios</c> in the integration suite, which compiles this very file). Each scenario asserts end states,
/// never interleavings, and waits on facts, settling or answers, never on time.
/// </summary>
public abstract class MeshScenarios
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>Starts the mesh a scenario needs.</summary>
    /// <param name="setup">The nodes, which of them start without an identity, and which pairs never hear each other.</param>
    /// <returns>The running mesh; the scenario disposes it.</returns>
    protected abstract Task<IMeshFixture> StartAsync(Setup setup);

    // 1. Pair (PAIR-SPEC.md, §9.1): both sides show the same code, each then knows the other user by the name it showed,
    // and the two devices establish one session each.
    [Fact]
    public async Task TwoUsersWhoPairSeeTheSameCode_KnowEachOtherByName_AndEstablishOneSessionEach()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop", "bob-desktop"));

        string sas = await mesh.PairAsync("alice-laptop", "bob-desktop", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);

        Assert.Matches("^[A-Z2-7]{4}-[A-Z2-7]{4}$", sas);
        Assert.Equal("bob", NameOf(await KnownUsersAsync(mesh, "alice-laptop"), "bob-desktop"));
        Assert.Equal("alice", NameOf(await KnownUsersAsync(mesh, "bob-desktop"), "alice-laptop"));
        Assert.True(MeshWaits.IsEstablishedWith(mesh.Node("alice-laptop"), DeviceOf("bob-desktop")));
        Assert.True(MeshWaits.IsEstablishedWith(mesh.Node("bob-desktop"), DeviceOf("alice-laptop")));
    }

    // 2. Create (LOCAL-SPEC.md §6.3): a Package created on a device that holds the user key is owned by that user, followed
    // there, and listed by the command line.
    [Fact]
    public async Task APackageCreatedOnADeviceIsOwnedByItsUserFollowedThereAndListed()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop"));

        PackageId channel = await CreateAsync(mesh, "alice-laptop", "Family", PackageKind.Channel);

        LocalPackage created = await PackageAsync(mesh, "alice-laptop", channel);
        Assert.Equal((LocalPackageState.Followed, UserOf("alice-laptop"), PackageKind.Channel, "Family"), (created.State, created.OwnerId, created.Kind, created.Name));
        CliResult listed = await mesh.CliAsync("alice-laptop", "package list", None);
        Assert.Equal(CliExitCodeOk, (int)listed.ExitCode);
        Assert.Contains("Family", listed.Stdout, StringComparison.Ordinal);
    }

    // 3. Write (SPEC.md §9.3, LOCAL-SPEC.md §6.7): two messages written from the command line are the Channel's history in
    // order, the second a child of the first with a higher clock.
    [Fact]
    public async Task TwoMessagesWrittenFromTheCommandLineAreTheChannelsHistoryInOrder()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop"));
        PackageId channel = await CreateAsync(mesh, "alice-laptop", "Family", PackageKind.Channel);

        ObjectHash first = await WriteAsync(mesh, "alice-laptop", channel, "hello");
        ObjectHash second = await WriteAsync(mesh, "alice-laptop", channel, "and again");

        IReadOnlyList<LocalEntry> history = await HistoryAsync(mesh, "alice-laptop", channel);
        Assert.Equal([first, second], history.Select(e => e.Hash));
        Assert.Equal([first], history[1].Parents);
        Assert.True(history[1].Clock > history[0].Clock);
    }

    // 4. Follow and replicate (SPEC.md §10, Appendix B): a follower comes to hold what the owner wrote; a message the phone
    // writes reaches Bob through the laptop, which sends the phone nothing about it but its Want.
    [Fact]
    public async Task AFollowerHoldsWhatTheOwnerWrote_AndAThirdDeviceRelaysWithoutSendingBackToTheSource()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop", "alice-phone", "bob-desktop") with { Severed = [("alice-phone", "bob-desktop")] });
        await mesh.PairAsync("alice-laptop", "bob-desktop", None);
        await mesh.ConnectAsync("alice-laptop", "alice-phone", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        PackageId channel = await CreateAsync(mesh, "alice-laptop", "Family", PackageKind.Channel);
        await GrantAsync(mesh, "alice-laptop", channel, "bob-desktop", GrantRights.Read);
        ObjectHash hello = await WriteAsync(mesh, "alice-laptop", channel, "hello");
        await mesh.SettleAsync(None);
        await FollowAsync(mesh, "alice-phone", channel);
        await FollowAsync(mesh, "bob-desktop", channel);
        await mesh.SettleAsync(None);
        long handledByPhone = HandledFrom(mesh, "alice-phone", "alice-laptop");

        ObjectHash typed = await WriteAsync(mesh, "alice-phone", channel, "typed on the phone");
        await mesh.SettleAsync(None);

        Assert.Equal([hello, typed], (await HistoryAsync(mesh, "bob-desktop", channel)).Select(e => e.Hash));
        Assert.Equal(1, HandledFrom(mesh, "alice-phone", "alice-laptop") - handledByPhone); // the laptop's Want, nothing else
        Assert.DoesNotContain(mesh.Node("bob-desktop").Replication.Sessions, s => s.Device == DeviceOf("alice-phone"));
    }

    // 5. Offline and online, both ways (SPEC.md §10.2): a grantee that was offline catches up with what the owner wrote,
    // and an owner that was offline catches up with what its write grantee wrote; both end with the same history.
    [Fact]
    public async Task WhatOneSideWroteWhileTheOtherWasOfflineArrivesOnceItIsBack_InBothDirections()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop", "bob-desktop"));
        await mesh.PairAsync("alice-laptop", "bob-desktop", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        PackageId channel = await CreateAsync(mesh, "alice-laptop", "Family", PackageKind.Channel);
        await GrantAsync(mesh, "alice-laptop", channel, "bob-desktop", GrantRights.Write);
        await mesh.SettleAsync(None);
        await FollowAsync(mesh, "bob-desktop", channel);
        await mesh.SettleAsync(None);

        await mesh.StopNodeAsync("bob-desktop", None);
        ObjectHash byAlice = await WriteAsync(mesh, "alice-laptop", channel, "while Bob was away");
        await mesh.StartNodeAsync("bob-desktop", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await mesh.SettleAsync(None);
        await mesh.StopNodeAsync("alice-laptop", None);
        ObjectHash byBob = await WriteAsync(mesh, "bob-desktop", channel, "while Alice was away");
        await mesh.StartNodeAsync("alice-laptop", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await mesh.SettleAsync(None);

        ObjectHash[] expected = [byAlice, byBob];
        Assert.Equal(expected, (await HistoryAsync(mesh, "alice-laptop", channel)).Select(e => e.Hash));
        Assert.Equal(expected, (await HistoryAsync(mesh, "bob-desktop", channel)).Select(e => e.Hash));
    }

    // 6. Revoke (SPEC.md §9.6, §9.7): the revoked user keeps what it holds, receives the revocation Grant, is no longer
    // listed the Package, and is not served what the owner writes afterwards.
    [Fact]
    public async Task ARevokedUserKeepsWhatItHolds_ReceivesTheRevocation_AndIsNoLongerListedOrServed()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop", "bob-desktop"));
        await mesh.PairAsync("alice-laptop", "bob-desktop", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        PackageId channel = await CreateAsync(mesh, "alice-laptop", "Family", PackageKind.Channel);
        await GrantAsync(mesh, "alice-laptop", channel, "bob-desktop", GrantRights.Read);
        ObjectHash before = await WriteAsync(mesh, "alice-laptop", channel, "before the revocation");
        await mesh.SettleAsync(None);
        await FollowAsync(mesh, "bob-desktop", channel);
        await mesh.SettleAsync(None);

        LocalClient alice = await mesh.ConnectUserAsync("alice-laptop", "scenario", None);
        await Ok(alice.SendAsync(new RevokeGrant { PackageId = channel, GranteeId = UserOf("bob-desktop") }, None), "RevokeGrant");
        await mesh.SettleAsync(None);
        ObjectHash after = await WriteAsync(mesh, "alice-laptop", channel, "after the revocation");
        await mesh.SettleAsync(None);

        LocalPackage held = await PackageAsync(mesh, "bob-desktop", channel);
        Assert.Equal(GrantRights.None, held.Rights);
        Assert.Equal(0, held.ListedBy);
        Assert.Equal([before], (await HistoryAsync(mesh, "bob-desktop", channel)).Select(e => e.Hash));
        Assert.False(await mesh.Node("bob-desktop").ContainsAsync(after, None));
    }

    // 7. Remove a device (SPEC.md §8.8, §4.3): once its user's Roster removes it, the device's session is closed, and every
    // later handshake it tries is refused, before and after the remaining device restarts.
    [Fact]
    public async Task ARemovedDeviceIsClosedAndRefusedForEver()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop", "alice-phone"));
        await mesh.ConnectAsync("alice-laptop", "alice-phone", None);
        await mesh.HoldUserKeyAsync("alice-laptop", None);
        LocalClient laptop = await mesh.ConnectUserAsync("alice-laptop", "scenario", None);

        Device removed = await Ok(laptop.SendAsync(new RemoveDevice { DeviceId = DeviceOf("alice-phone") }, None), "RemoveDevice");
        await mesh.WaitUntilAsync("alice-laptop", n => n.Peers.All(p => p.Device != DeviceOf("alice-phone") || p.Sessions == 0), "the phone's session closed", None);

        Assert.True(removed.Removed);
        await ExpectRefusedAsync(mesh, "alice-laptop", "alice-phone");
        await mesh.StopNodeAsync("alice-laptop", None);
        await mesh.StartNodeAsync("alice-laptop", None);
        await ExpectRefusedAsync(mesh, "alice-laptop", "alice-phone");
    }

    // 8. Duplicate connections (SPEC.md §4.5): a device that connects a second time while its first session lives keeps
    // the newer one; the older is closed with Bye(Duplicate), and the node holds one session to the device.
    [Fact]
    public async Task ASecondConnectionOfOneDeviceReplacesTheFirst_WhichIsClosedAsADuplicate()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop"));
        TestIdentity phone = TestIdentity.Of("alice-phone");

        await using ScriptedPeer first = await EstablishAsync(mesh, "alice-laptop", phone);
        await using ScriptedPeer second = await EstablishAsync(mesh, "alice-laptop", phone);

        Assert.Equal(WireByeReason.Duplicate, (await first.ExpectByeAsync(None)).Reason);
        await mesh.WaitUntilAsync("alice-laptop", n => MeshWaits.IsEstablishedWith(n, phone.DeviceId), "one session to the phone", None);
    }

    // 9. Slow consumer (LOCAL-SPEC.md §7.4): a client that never reads its subscription loses that subscription; its
    // connection keeps answering, and replication stores every Entry regardless.
    [Fact]
    public async Task AClientThatStopsReadingLosesItsSubscription_TheConnectionAndTheMeshCarryOn()
    {
        const int Messages = 300;
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop", "bob-desktop"));
        await mesh.PairAsync("alice-laptop", "bob-desktop", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        PackageId channel = await CreateAsync(mesh, "alice-laptop", "Family", PackageKind.Channel);
        await GrantAsync(mesh, "alice-laptop", channel, "bob-desktop", GrantRights.Read);
        await mesh.SettleAsync(None);
        await FollowAsync(mesh, "bob-desktop", channel);
        LocalClient watcher = await mesh.ConnectUserAsync("bob-desktop", "never reads", None);
        Subscription<EntryStored> unread = await Ok(watcher.SubscribeAsync<EntryStored>(Filter.Any with { PackageId = channel }, None), "Subscribe");

        LocalClient writer = await mesh.ConnectCliServiceAsync("alice-laptop", None);
        await Ok(writer.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message] }, None), "RegisterKind");
        for (int i = 0; i < Messages; i++)
            await Ok(writer.SendAsync(WriteEntry.Of(channel, Message($"message {i}").ToBody()), None), "WriteEntry");
        await mesh.SettleAsync(None);

        Assert.True((await watcher.SendAsync(new GetIdentity(), None)).IsOk); // answered after every event before it
        Assert.NotNull(unread.Lapse);
        Assert.Equal((ulong)Messages, (await PackageAsync(mesh, "bob-desktop", channel)).Entries);
    }

    // 10. Eve (SPEC.md §4.3, §3): a device of a user nobody knows is refused before a protocol byte, and while the laptop
    // dials the phone it hears of, it never dials Eve, whose announcement it hears as well.
    [Fact]
    public async Task EveIsRefusedBeforeAProtocolByte_AndNeverDialled()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop", "alice-phone", "eve-device"));
        DeviceId eve = DeviceOf("eve-device");

        await ExpectRefusedAsync(mesh, "alice-laptop", "eve-device");
        await mesh.ConnectAsync("alice-laptop", "alice-phone", None);

        MeshNode laptop = mesh.Node("alice-laptop");
        Assert.DoesNotContain(laptop.Facts.Recent(), f => f.Device == eve && f.Kind is NodeFactKind.Dialling or NodeFactKind.SessionOpened or NodeFactKind.Established);
        Assert.DoesNotContain(laptop.Peers, p => p.Device == eve);
    }

    // 11. Forged content (SPEC.md §12.2, §7.6, §12.1): an Entry whose clock jumps more than 2^32 above its parent is dropped
    // as faulty while the session goes on, and is never forwarded to Bob; an Object whose hash is not its bytes' closes
    // the session with Bye(Violation).
    [Fact]
    public async Task AJumpedClockIsDroppedAsFaultyAndNeverForwarded_AWrongHashClosesTheSessionAsAViolation()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop", "bob-desktop"));
        await mesh.PairAsync("alice-laptop", "bob-desktop", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        PackageId channel = await CreateAsync(mesh, "alice-laptop", "Family", PackageKind.Channel);
        await GrantAsync(mesh, "alice-laptop", channel, "bob-desktop", GrantRights.Read);
        ObjectHash genuine = await WriteAsync(mesh, "alice-laptop", channel, "genuine");
        await mesh.SettleAsync(None);
        await FollowAsync(mesh, "bob-desktop", channel);
        await mesh.SettleAsync(None);
        LocalPackage listed = await PackageAsync(mesh, "alice-laptop", channel);
        TestIdentity phone = TestIdentity.Of("alice-phone");
        VerifiedEntry forged = new EntryDraft
        {
            Package = channel,
            Clock = Limits.MaxClockGap + 1,
            Parents = EntryParents.Of(genuine),
            Body = Message("from the far future").ToBody(),
        }.Sign(phone.Device);

        await using ScriptedPeer forger = await EstablishAsync(mesh, "alice-laptop", phone);
        long before = mesh.Node("alice-laptop").Facts.LastSequence;
        await forger.SendAsync(new WireChanged(new WireState(channel, listed.ManifestHash, listed.ManifestSeq, [], [new Head(phone.DeviceId, forged.Hash, forged.Content.Clock)])), None);
        Assert.Equal([forged.Hash], (await forger.ExpectAsync<WireWant>(None)).Hashes);
        await forger.SendAsync(WireObject.FromStored(forged.Hash, forged.Bytes), None);
        await mesh.WaitUntilAsync("alice-laptop", n => n.Facts.Recent().Any(f => f.Sequence > before && f.Kind == NodeFactKind.FaultyContent && f.Subject == forged.Hash.Value), "the forged Entry recorded as faulty", None);
        await forger.SendAsync(WireObject.FromStored(genuine, forged.Bytes), None);
        WireBye bye = await forger.ExpectByeAsync(None);
        await mesh.SettleAsync(None);

        Assert.Equal(WireByeReason.Violation, bye.Reason);
        Assert.False(await mesh.Node("alice-laptop").ContainsAsync(forged.Hash, None));
        Assert.False(await mesh.Node("bob-desktop").ContainsAsync(forged.Hash, None));
    }

    // 12. Eviction (SPEC.md §11.2, §11.3): what a device no longer follows is unpinned, and a tiny budget evicts it until
    // nothing evictable is left, while the owner's device keeps everything it published.
    [Fact]
    public async Task UnderATinyBudgetEverythingUnpinnedIsEvicted_TheOwnerKeepsWhatItPublished()
    {
        string folder = Directory.CreateTempSubdirectory("axiom-mesh-scenario-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "a.bin"), [.. Enumerable.Range(0, (2 << 20) + 5).Select(i => (byte)((i * 31) ^ (i >> 8) ^ (i >> 20)))]); // three distinct Chunks
            File.WriteAllText(Path.Combine(folder, "b.txt"), "small");
            await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop", "bob-desktop"));
            await mesh.PairAsync("alice-laptop", "bob-desktop", None);
            await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
            PackageId photos = await CreateAsync(mesh, "alice-laptop", "Photos", PackageKind.Archive);
            await GrantAsync(mesh, "alice-laptop", photos, "bob-desktop", GrantRights.Read);
            await CliOkAsync(mesh, "alice-laptop", $"package publish '{folder}' --package {photos.Value.ToHex()}");
            await mesh.SettleAsync(None);
            await FollowAsync(mesh, "bob-desktop", photos);
            await mesh.SettleAsync(None);
            Store full = await StoreAsync(mesh, "bob-desktop");
            Store owner = await StoreAsync(mesh, "alice-laptop");
            LocalClient bob = await mesh.ConnectUserAsync("bob-desktop", "scenario", None);

            await Ok(bob.SendAsync(new UnfollowPackage { PackageId = photos }, None), "UnfollowPackage");
            await Ok(bob.SendAsync(new SetStoreBudget { Bytes = 1 }, None), "SetStoreBudget");
            await mesh.WaitUntilAsync("bob-desktop", n => n.Facts.Recent().Any(f => f.Kind == NodeFactKind.ObjectEvicted), "an eviction", None);
            await mesh.SettleAsync(None);

            Store evicted = await StoreAsync(mesh, "bob-desktop");
            Assert.True(full.Bytes > (2 << 20), $"Bob held {full.Bytes} bytes after following.");
            Assert.True(evicted.Bytes < full.Bytes / 2, $"Bob holds {evicted.Bytes} of {full.Bytes} bytes after eviction.");
            Assert.Equal(0ul, evicted.EvictableBytes);
            Assert.Equal(owner.Bytes, (await StoreAsync(mesh, "alice-laptop")).Bytes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 13. Enrol a second device (PAIR-SPEC.md §9.2, §9.3): the new device holds the user's identity but not the user key,
    // the user's first device lists it, and the two replicate the user's Channel.
    [Fact]
    public async Task AnEnrolledDeviceHoldsTheUsersIdentityButNotItsKey_AndReplicatesItsPackages()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop", "alice-phone") with { WithoutIdentity = ["alice-phone"] });

        await mesh.EnrolAsync("alice-laptop", "alice-phone", None);
        await mesh.ConnectAsync("alice-laptop", "alice-phone", None);
        PackageId channel = await CreateAsync(mesh, "alice-laptop", "Family", PackageKind.Channel);
        ObjectHash written = await WriteAsync(mesh, "alice-laptop", channel, "to my phone");
        await mesh.SettleAsync(None);
        await FollowAsync(mesh, "alice-phone", channel);
        await mesh.SettleAsync(None);

        Identity identity = await Ok((await mesh.ConnectUserAsync("alice-phone", "scenario", None)).SendAsync(new GetIdentity(), None), "GetIdentity");
        Assert.Equal((true, UserOf("alice-laptop"), false), (identity.Present, identity.UserId, identity.UserKeyPresent));
        IReadOnlyList<Device> devices = await Ok((await mesh.ConnectUserAsync("alice-laptop", "scenario", None)).SendAsync(new ListDevices(), None), "ListDevices");
        Assert.Contains(devices, d => d.DeviceId == DeviceOf("alice-phone") && !d.Removed);
        Assert.Equal([written], (await HistoryAsync(mesh, "alice-phone", channel)).Select(e => e.Hash));
    }

    // 14. Restart (SPEC.md §11.1, LOCAL-SPEC.md §5.6): stopped and started again, both devices still know each other, hold
    // the same history, reconnect without pairing again, and replicate what is written afterwards.
    [Fact]
    public async Task ARestartKeepsUsersPackagesAndHistory_AndReplicationResumes()
    {
        await using IMeshFixture mesh = await StartAsync(Setup.Of("alice-laptop", "bob-desktop"));
        await mesh.PairAsync("alice-laptop", "bob-desktop", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        PackageId channel = await CreateAsync(mesh, "alice-laptop", "Family", PackageKind.Channel);
        await GrantAsync(mesh, "alice-laptop", channel, "bob-desktop", GrantRights.Read);
        ObjectHash first = await WriteAsync(mesh, "alice-laptop", channel, "first");
        await mesh.SettleAsync(None);
        await FollowAsync(mesh, "bob-desktop", channel);
        await mesh.SettleAsync(None);

        await mesh.StopNodeAsync("alice-laptop", None);
        await mesh.StopNodeAsync("bob-desktop", None);
        await mesh.StartNodeAsync("alice-laptop", None);
        await mesh.StartNodeAsync("bob-desktop", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        ObjectHash second = await WriteAsync(mesh, "alice-laptop", channel, "second");
        await mesh.SettleAsync(None);

        Assert.Equal("bob", NameOf(await KnownUsersAsync(mesh, "alice-laptop"), "bob-desktop"));
        Assert.Equal(LocalPackageState.Followed, (await PackageAsync(mesh, "bob-desktop", channel)).State);
        Assert.Equal([first, second], (await HistoryAsync(mesh, "bob-desktop", channel)).Select(e => e.Hash));
    }

    private const int CliExitCodeOk = 0;

    private static DeviceId DeviceOf(string node) => TestIdentity.Of(node).DeviceId;

    private static UserId UserOf(string node) => TestIdentity.Of(node).UserId;

    private static MessagePayload Message(string text) => new() { Text = text, Attachments = [], ReplyTo = default };

    private static string NameOf(IReadOnlyList<User> users, string node) =>
        users.FirstOrDefault(u => u.UserId == UserOf(node))?.Name ?? throw new InvalidOperationException($"The user of {node} is not known.");

    private static async Task<T> Ok<T>(ValueTask<LocalResult<T>> pending, string request)
    {
        LocalResult<T> result = await pending;
        Assert.True(result.IsOk, $"{request} was refused with {result.Status}: {result.Error?.Message}");
        return result.Value;
    }

    private static async Task<CliResult> CliOkAsync(IMeshFixture mesh, string node, string commandLine)
    {
        CliResult result = await mesh.CliAsync(node, commandLine, None);
        Assert.True((int)result.ExitCode == CliExitCodeOk, $"axiom-mesh {commandLine} on {node}: {result}");
        return result;
    }

    // The node holds the user key and creates the Package over its local port.
    private static async Task<PackageId> CreateAsync(IMeshFixture mesh, string node, string name, PackageKind kind)
    {
        await mesh.HoldUserKeyAsync(node, None);
        await using LocalClient client = await mesh.ConnectUserAsync(node, "scenario", None);
        return (await Ok(client.SendAsync(new CreatePackage { Kind = kind, Name = name }, None), "CreatePackage")).PackageId;
    }

    private static async Task GrantAsync(IMeshFixture mesh, string owner, PackageId package, string grantee, GrantRights rights)
    {
        await using LocalClient client = await mesh.ConnectUserAsync(owner, "scenario", None);
        await Ok(client.SendAsync(new SetGrant { PackageId = package, GranteeId = UserOf(grantee), Rights = rights }, None), "SetGrant");
    }

    // A Package is followed once a peer listed it (LOCAL-SPEC.md §6.3): the caller connected and settled first.
    private static async Task FollowAsync(IMeshFixture mesh, string node, PackageId package)
    {
        await using LocalClient client = await mesh.ConnectUserAsync(node, "scenario", None);
        await Ok(client.SendAsync(new FollowPackage { PackageId = package }, None), "FollowPackage");
    }

    // A message written by the command line, as the cli service of the node: the Entry's hash from what it printed.
    private static async Task<ObjectHash> WriteAsync(IMeshFixture mesh, string node, PackageId package, string text)
    {
        CliResult written = await CliOkAsync(mesh, node, $"write {package.Value.ToHex()} --text '{text}'");
        Match entry = Regex.Match(written.Stdout, "entry ([0-9a-f]{64})");
        Assert.True(entry.Success && Hash32.TryParseHex(entry.Groups[1].Value, out _), written.ToString());
        return new ObjectHash(Hash32.TryParseHex(entry.Groups[1].Value, out Hash32 hash) ? hash : default);
    }

    private static async Task<IReadOnlyList<LocalEntry>> HistoryAsync(IMeshFixture mesh, string node, PackageId package)
    {
        await using LocalClient client = await mesh.ConnectUserAsync(node, "scenario", None);
        return (await Ok(client.SendAsync(new ListHistory { PackageId = package, Limit = 64 }, None), "ListHistory")).Entries;
    }

    private static async Task<LocalPackage> PackageAsync(IMeshFixture mesh, string node, PackageId package)
    {
        await using LocalClient client = await mesh.ConnectUserAsync(node, "scenario", None);
        return await Ok(client.SendAsync(new GetPackage { PackageId = package }, None), "GetPackage");
    }

    private static async Task<IReadOnlyList<User>> KnownUsersAsync(IMeshFixture mesh, string node)
    {
        await using LocalClient client = await mesh.ConnectUserAsync(node, "scenario", None);
        return await Ok(client.SendAsync(new ListKnownUsers(), None), "ListKnownUsers");
    }

    private static async Task<Store> StoreAsync(IMeshFixture mesh, string node)
    {
        await using LocalClient client = await mesh.ConnectUserAsync(node, "scenario", None);
        return await Ok(client.SendAsync(new GetStore(), None), "GetStore");
    }

    private static long HandledFrom(IMeshFixture mesh, string node, string peer) =>
        mesh.Node(node).Replication.Sessions.Single(s => s.Device == DeviceOf(peer)).MessagesHandled;

    // A scripted device that said Hello and was answered: the node's Hello, then its listing.
    private static async Task<ScriptedPeer> EstablishAsync(IMeshFixture mesh, string target, TestIdentity identity)
    {
        ScriptedPeer peer = await mesh.ConnectScriptedPeerAsync(target, identity, None);
        await peer.SendAsync(peer.Hello(1), None);
        await peer.ExpectAsync<WireHello>(None);
        await peer.ExpectAsync<WirePackages>(None);
        return peer;
    }

    // SPEC.md §4.3: the node closes the connection before a single protocol byte.
    private static async Task ExpectRefusedAsync(IMeshFixture mesh, string target, string device)
    {
        await using ScriptedPeer peer = await mesh.ConnectScriptedPeerAsync(target, TestIdentity.Of(device), None);
        await peer.ExpectClosedWithoutHelloAsync(None);
    }

    /// <summary>The mesh a scenario starts.</summary>
    protected sealed record Setup
    {
        /// <summary>The nodes, by published device name.</summary>
        public required IReadOnlyList<string> Nodes { get; init; }

        /// <summary>The nodes that start without an identity, to be enrolled.</summary>
        public IReadOnlyList<string> WithoutIdentity { get; init; } = [];

        /// <summary>Pairs of nodes that never hear of each other: no announcement of one reaches the other.</summary>
        public IReadOnlyList<(string A, string B)> Severed { get; init; } = [];

        /// <summary>A mesh of the named nodes, each a device of its own user.</summary>
        /// <param name="nodes">The nodes.</param>
        /// <returns>The setup.</returns>
        public static Setup Of(params string[] nodes) => new() { Nodes = nodes };
    }
}
