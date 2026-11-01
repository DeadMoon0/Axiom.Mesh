using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// The chat service's scenario suite (plan §9.3 item 5, CHAT-SERVICE.md §12): what Alice's laptop and Bob's desktop show of
/// a shared Channel after both send with attachments, go offline, withdraw, reply, restart the service, change store epoch
/// and unfollow. Written once against <see cref="IMeshFixture"/> with the chat service running in process beside each node,
/// it runs unchanged in memory (<c>InMemoryChatScenarios</c>) and on real sockets with the real pipe
/// (<c>SocketChatScenarios</c> in the integration suite, which compiles this very file). Every wait is on a settled mesh and
/// the service's <c>about.CaughtUp</c>, never on time.
/// </summary>
public abstract class ChatScenarios
{
    private const string Alice = "alice-laptop";
    private const string Bob = "bob-desktop";
    private const int CliOk = 0;
    private const int CliRefused = 3;

    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>Starts a mesh of the named nodes whose local ports list the chat service beside <c>cli</c>.</summary>
    /// <param name="nodes">The nodes, by published device name.</param>
    /// <returns>The running mesh; the scenario disposes it.</returns>
    protected abstract Task<IMeshFixture> StartAsync(IReadOnlyList<string> nodes);

    // 1. A Channel shared by Alice and Bob is indexed by the chat service on both ends, and `chat list` shows it.
    [Fact]
    public async Task ASharedChannelIsIndexedOnBothEnds_AndChatListShowsIt()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh);
        await using RunningService aliceChat = await RunChatAsync(mesh, Alice);
        await using RunningService bobChat = await RunChatAsync(mesh, Bob);

        foreach (string node in new[] { Alice, Bob })
        {
            await UntilAsync(mesh, node, "the Channel indexed", async app => (await ChannelsAsync(app)).Any(c => c.PackageId == channel && c.Followed && c.Indexed));
            CliResult listed = await CliOkAsync(mesh, node, "chat list");
            Assert.Contains("Family", listed.Stdout, StringComparison.Ordinal);
            Assert.Contains("\"indexed\": true", (await CliOkAsync(mesh, node, "chat list --json")).Stdout, StringComparison.Ordinal);
        }
    }

    // 2. Both send with attachments (a 1 MiB file, a folder): both show the same messages under the same keys in the same
    // order (SPEC.md §9.3), each attachment with its size (a folder's is the sum of its files', S-D10).
    [Fact]
    public async Task BothSendWithAttachments_AndBothShowTheSameKeysInTheSameOrder()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh);
        await using RunningService aliceChat = await RunChatAsync(mesh, Alice);
        await using RunningService bobChat = await RunChatAsync(mesh, Bob);
        using var files = new TempFolder();
        string photo = files.File("photo.bin", 1_048_576, 7);
        string folder = files.Folder("docs", ("a.txt", 3_000), ("sub/b.bin", 20_000));

        ObjectHash fromAlice = await SendByCliAsync(mesh, Alice, $"chat send {Hex(channel)} --text 'a photo' --file '{photo}'");
        ObjectHash fromBob = await SendByCliAsync(mesh, Bob, $"chat send {Hex(channel)} --text 'the docs' --file '{folder}'");
        await mesh.SettleAsync(None);

        IReadOnlyList<MessageView> atAlice = await TimelineWhenAsync(mesh, Alice, channel, t => t.Count == 2);
        IReadOnlyList<MessageView> atBob = await TimelineWhenAsync(mesh, Bob, channel, t => t.Count == 2);
        Assert.Equal(atAlice.Select(m => m.Key), atBob.Select(m => m.Key));
        Assert.Equal(new[] { fromAlice, fromBob }.Order(), atAlice.Select(m => m.Key.Hash).Order());
        Assert.True(atAlice[0].Key < atAlice[1].Key);
        MessageView photoMessage = atBob.Single(m => m.Key.Hash == fromAlice);
        MessageView docsMessage = atAlice.Single(m => m.Key.Hash == fromBob);
        Assert.Equal(("a photo", "photo.bin", 1_048_576ul), (photoMessage.Text, photoMessage.Attachments.Single().Name, photoMessage.Attachments.Single().Size));
        Assert.Equal(("the docs", "docs", 23_000ul), (docsMessage.Text, docsMessage.Attachments.Single().Name, docsMessage.Attachments.Single().Size));
    }

    // 3. Messages written while the other device was offline, on both sides, slot in by clock (SPEC.md §9.3): both end
    // with the same timeline in the same order.
    [Fact]
    public async Task MessagesWrittenWhileTheOtherWasOfflineSlotInByClock_IdenticallyOnBoth()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh);
        RunningService aliceChat = await RunChatAsync(mesh, Alice);
        RunningService bobChat = await RunChatAsync(mesh, Bob);
        await SendAsync(mesh, Alice, channel, "first");
        await mesh.SettleAsync(None);
        await SendAsync(mesh, Bob, channel, "second");
        await mesh.SettleAsync(None);

        await bobChat.DisposeAsync();
        await mesh.StopNodeAsync(Bob, None);
        ObjectHash whileBobAway = await SendAsync(mesh, Alice, channel, "while Bob was away");
        await aliceChat.DisposeAsync();
        await mesh.StopNodeAsync(Alice, None);
        await mesh.StartNodeAsync(Bob, None);
        await using (RunningService alone = await RunChatAsync(mesh, Bob))
            await SendAsync(mesh, Bob, channel, "while Alice was away");
        await mesh.StartNodeAsync(Alice, None);
        await mesh.ConnectAsync(Alice, Bob, None);
        await mesh.SettleAsync(None);
        await using RunningService aliceAgain = await RunChatAsync(mesh, Alice);
        await using RunningService bobAgain = await RunChatAsync(mesh, Bob);

        IReadOnlyList<MessageView> atAlice = await TimelineWhenAsync(mesh, Alice, channel, t => t.Count == 4);
        IReadOnlyList<MessageView> atBob = await TimelineWhenAsync(mesh, Bob, channel, t => t.Count == 4);
        Assert.Equal(atAlice.Select(m => m.Key), atBob.Select(m => m.Key));
        Assert.Equal(atAlice.Select(m => m.Key).Order(), atAlice.Select(m => m.Key));
        Assert.Equal(["first", "second"], atBob.Take(2).Select(m => m.Text));
        Assert.Contains(whileBobAway, atBob.Select(m => m.Key.Hash));
    }

    // 4. Bob withdraws his own message (SPEC.md §9.4): both show it withdrawn and never its text, in text and JSON alike.
    [Fact]
    public async Task BobWithdrawsHisOwnMessage_BothShowItWithdrawn_NeverTheText()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh);
        await using RunningService aliceChat = await RunChatAsync(mesh, Alice);
        await using RunningService bobChat = await RunChatAsync(mesh, Bob);
        ObjectHash secret = await SendAsync(mesh, Bob, channel, "a secret");
        await mesh.SettleAsync(None);

        await CliOkAsync(mesh, Bob, $"chat withdraw {secret.Value.ToHex()}");
        await mesh.SettleAsync(None);

        foreach (string node in new[] { Alice, Bob })
        {
            MessageView shown = (await TimelineWhenAsync(mesh, node, channel, t => t.Count == 1 && t[0].Withdrawn)).Single();
            Assert.Equal((string.Empty, 0, DeviceOf(Bob)), (shown.Text, shown.Attachments.Count, shown.WithdrawnBy));
            CliResult text = await CliOkAsync(mesh, node, $"chat show {Hex(channel)}");
            CliResult json = await CliOkAsync(mesh, node, $"chat show {Hex(channel)} --json");
            Assert.Contains("(withdrawn)", text.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", text.Stdout + json.Stdout, StringComparison.Ordinal);
        }
    }

    // 5. The Channel's owner may withdraw anyone's message; Bob may not withdraw the owner's (NotYours, SPEC.md §9.4).
    [Fact]
    public async Task TheOwnerWithdrawsBobsMessage_AndBobCannotWithdrawTheOwners()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh);
        await using RunningService aliceChat = await RunChatAsync(mesh, Alice);
        await using RunningService bobChat = await RunChatAsync(mesh, Bob);
        ObjectHash byAlice = await SendAsync(mesh, Alice, channel, "from the owner");
        ObjectHash byBob = await SendAsync(mesh, Bob, channel, "from a guest");
        await mesh.SettleAsync(None);
        await TimelineWhenAsync(mesh, Bob, channel, t => t.Count == 2);

        CliResult refused = await mesh.CliAsync(Bob, $"chat withdraw {byAlice.Value.ToHex()}", None);
        LocalResult<WithdrawReply> notYours = await TryCallAsync(mesh, Bob, new WithdrawRequest { Message = byAlice });
        await CliOkAsync(mesh, Alice, $"chat withdraw {byBob.Value.ToHex()}");
        await mesh.SettleAsync(None);

        Assert.Equal(CliRefused, (int)refused.ExitCode);
        Assert.Contains("only the device that wrote the message", refused.Stderr, StringComparison.Ordinal);
        Assert.Equal((Status)(ushort)ChatStatus.NotYours, notYours.Status);
        foreach (string node in new[] { Alice, Bob })
        {
            IReadOnlyList<MessageView> shown = await TimelineWhenAsync(mesh, node, channel, t => t.Any(m => m.Withdrawn));
            Assert.Equal([(byAlice, false), (byBob, true)], shown.Select(m => (m.Key.Hash, m.Withdrawn)).OrderBy(p => p.Item1 == byBob));
            Assert.Equal(DeviceOf(Alice), shown.Single(m => m.Key.Hash == byBob).WithdrawnBy);
        }
    }

    // 6. A Redaction Bob's node stores before its target (heads replicate first) is kept and applied once the target is
    // indexed: the message is never shown, whatever arrived first (SPEC.md §9.4, both orders).
    [Fact]
    public async Task ARedactionStoredBeforeItsTargetIsAppliedWhenTheTargetArrives()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh, followByBob: false);
        await using RunningService aliceChat = await RunChatAsync(mesh, Alice);
        await using RunningService bobChat = await RunChatAsync(mesh, Bob);
        ObjectHash target = await SendAsync(mesh, Alice, channel, "said too soon");
        await CallAsync(mesh, Alice, new WithdrawRequest { Message = target });
        await mesh.SettleAsync(None);

        await FollowAsync(mesh, Bob, channel);
        await mesh.SettleAsync(None);

        MessageView atBob = (await TimelineWhenAsync(mesh, Bob, channel, t => t.Count == 1 && t[0].Withdrawn)).Single();
        Assert.Equal((target, string.Empty, true), (atBob.Key.Hash, atBob.Text, atBob.Withdrawn));
        ChannelView indexed = (await CallAsync(mesh, Bob, new ChannelsRequest())).Channels.Single();
        Assert.Equal((1ul, 1ul), (indexed.Messages, indexed.Withdrawn));
        await using LocalClient reader = await mesh.ConnectUserAsync(Bob, "chat-scenario", None);
        IReadOnlyList<Entry> stored = Ok(await reader.SendAsync(new ListStored { PackageId = channel, Limit = 16 }, None), "ListStored").Entries;
        Assert.Equal([EntryKind.Redaction, EntryKind.Message], stored.Select(e => e.Kind));

    }

    // 7. An attached folder and an attached file, fetched by Bob with `chat get`, materialise byte-equal (SPEC.md §11.4).
    [Fact]
    public async Task AnAttachedFolderAndFileMaterialiseByteEqualOnTheOtherDevice()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh);
        await using RunningService aliceChat = await RunChatAsync(mesh, Alice);
        await using RunningService bobChat = await RunChatAsync(mesh, Bob);
        using var files = new TempFolder();
        string photo = files.File("photo.bin", 2_500_000, 11);
        string folder = files.Folder("album", ("one.txt", 500), ("deep/two.bin", 1_100_000));
        ObjectHash message = await SendByCliAsync(mesh, Alice, $"chat send {Hex(channel)} --text 'holiday' --file '{photo}' --file '{folder}'");
        await mesh.SettleAsync(None);
        await TimelineWhenAsync(mesh, Bob, channel, t => t.Count == 1);

        string gotPhoto = Path.Combine(files.Path, "got-photo.bin");
        string gotAlbum = Path.Combine(files.Path, "got-album");
        CliResult file = await CliOkAsync(mesh, Bob, $"chat get {message.Value.ToHex()} photo.bin --out '{gotPhoto}'");
        CliResult tree = await CliOkAsync(mesh, Bob, $"chat get {message.Value.ToHex()} album --out '{gotAlbum}'");

        Assert.Contains("written:", file.Stdout, StringComparison.Ordinal);
        Assert.Contains("written:", tree.Stdout, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(photo), File.ReadAllBytes(gotPhoto));
        foreach (string relative in new[] { "one.txt", Path.Combine("deep", "two.bin") })
            Assert.Equal(File.ReadAllBytes(Path.Combine(folder, relative)), File.ReadAllBytes(Path.Combine(gotAlbum, relative)));
    }

    // 8. A thousand messages from both page identically forward and backward on both devices (SPEC.md §9.3).
    [Fact]
    public async Task AThousandMessagesPageIdenticallyForwardAndBackward_OnBothDevices()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh);
        await using RunningService aliceChat = await RunChatAsync(mesh, Alice);
        await using RunningService bobChat = await RunChatAsync(mesh, Bob);
        await using (LocalClient atAlice = await mesh.ConnectUserAsync(Alice, "chat-scenario", None))
        await using (LocalClient atBob = await mesh.ConnectUserAsync(Bob, "chat-scenario", None))
        {
            for (int i = 0; i < 1_000; i++)
                Ok(await ChatCall.SendAsync(i % 5 < 3 ? atAlice : atBob, new SendRequest { PackageId = channel, Text = $"message {i}" }, None), "send");
        }

        await mesh.SettleAsync(None);

        var forward = new Dictionary<string, IReadOnlyList<EntryKey>>();
        foreach (string node in new[] { Alice, Bob })
        {
            await TimelineWhenAsync(mesh, node, channel, t => t.Count == 1_000);
            forward[node] = await PagedKeysAsync(mesh, node, channel, reverse: false, 97);
            IReadOnlyList<EntryKey> backward = await PagedKeysAsync(mesh, node, channel, reverse: true, 101);
            Assert.Equal(1_000, forward[node].Count);
            Assert.Equal(forward[node], backward.Reverse());
            Assert.Equal(forward[node].Order(), forward[node]);
        }

        Assert.Equal(forward[Alice], forward[Bob]);
    }

    // 9. The service restarted after more than 256 Entries were stored meanwhile catches up by store sequence, pages
    // first (LOCAL-SPEC.md §9.3), and holds every message exactly once.
    [Fact]
    public async Task AServiceRestartCatchesUpByStoreSequenceWithoutDuplicates_AcrossAGapOver256()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh);
        await using RunningService aliceChat = await RunChatAsync(mesh, Alice);
        RunningService bobChat = await RunChatAsync(mesh, Bob);
        for (int i = 0; i < 10; i++)
            await SendAsync(mesh, Alice, channel, $"before {i}");
        await mesh.SettleAsync(None);
        await TimelineWhenAsync(mesh, Bob, channel, t => t.Count == 10);

        await bobChat.DisposeAsync();
        await using (LocalClient atAlice = await mesh.ConnectUserAsync(Alice, "chat-scenario", None))
        {
            for (int i = 0; i < 300; i++)
                Ok(await ChatCall.SendAsync(atAlice, new SendRequest { PackageId = channel, Text = $"meanwhile {i}" }, None), "send");
        }

        await mesh.SettleAsync(None);
        await using RunningService restarted = await RunChatAsync(mesh, Bob);

        IReadOnlyList<MessageView> atBob = await TimelineWhenAsync(mesh, Bob, channel, t => t.Count >= 310);
        AboutReply about = await CallAsync(mesh, Bob, new AboutRequest());
        Assert.Equal(310, atBob.Count);
        Assert.Equal(310, atBob.Select(m => m.Key.Hash).Distinct().Count());
        Assert.Equal(310ul, (await CallAsync(mesh, Bob, new ChannelsRequest())).Channels.Single().Messages);
        Assert.True(about.IndexedSeq >= 310 && about.CaughtUp);
    }

    // 10. A service whose state meets another store epoch discards its index (LOCAL-SPEC.md §9.3): nothing of the store it
    // indexed before remains, and it rebuilds from the Host it serves now.
    [Fact]
    public async Task AnotherStoreEpochDiscardsTheIndex_AndItIsRebuiltFromTheHostItServesNow()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId shared = await SharedChannelAsync(mesh);
        RunningService aliceChat = await RunChatAsync(mesh, Alice);
        RunningService bobChat = await RunChatAsync(mesh, Bob);
        for (int i = 0; i < 3; i++)
            await SendAsync(mesh, Alice, shared, $"shared {i}");
        PackageId notes = await CreateAsync(mesh, Bob, "Bob's notes");
        await SendAsync(mesh, Bob, notes, "only on Bob's store");
        await mesh.SettleAsync(None);
        await UntilAsync(mesh, Bob, "both Channels indexed", async app => (await ChannelsAsync(app)).Count == 2 && (await TimelineAsync(app, shared)).Count == 3);
        await bobChat.DisposeAsync();
        await aliceChat.DisposeAsync();

        await using RunningService moved = await mesh.RunServiceAsync(Alice, new ChatService(mesh.Clock), Bob, None);

        await UntilAsync(mesh, Alice, "the index rebuilt from Alice's store", async app => (await ChannelsAsync(app)).Count == 1 && (await TimelineAsync(app, shared)).Count == 3);
        await using LocalClient app = await mesh.ConnectUserAsync(Alice, "chat-scenario", None);
        AboutReply about = Ok(await ChatCall.SendAsync(app, new AboutRequest(), None), "about");
        Assert.Equal(app.Host.StoreEpoch, about.StoreEpoch);
        Assert.Equal([shared], (await ChannelsAsync(app)).Select(c => c.PackageId));
        Assert.Equal((Status)(ushort)ChatStatus.NotAChannel, (await ChatCall.SendAsync(app, new TimelineRequest { PackageId = notes, Limit = 10 }, None)).Status);
    }

    // 11. A reply threads under its root, a reply to the reply too, and the thread survives the root's withdrawal.
    [Fact]
    public async Task AReplyThreadsUnderItsRoot_AndTheThreadSurvivesTheRootsWithdrawal()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh);
        await using RunningService aliceChat = await RunChatAsync(mesh, Alice);
        await using RunningService bobChat = await RunChatAsync(mesh, Bob);
        ObjectHash root = await SendAsync(mesh, Alice, channel, "dinner on friday?");
        await mesh.SettleAsync(None);
        await TimelineWhenAsync(mesh, Bob, channel, t => t.Count == 1);
        ObjectHash reply = await SendByCliAsync(mesh, Bob, $"chat send {Hex(channel)} --text 'yes, at eight' --reply {root.Value.ToHex()}");
        await mesh.SettleAsync(None);
        await TimelineWhenAsync(mesh, Alice, channel, t => t.Count == 2);
        ObjectHash second = (await CallAsync(mesh, Alice, new SendRequest { PackageId = channel, Text = "eight it is", ReplyTo = reply })).Key.Hash;
        await CallAsync(mesh, Alice, new WithdrawRequest { Message = root });
        await mesh.SettleAsync(None);

        foreach (string node in new[] { Alice, Bob })
        {
            await TimelineWhenAsync(mesh, node, channel, t => t.Count == 3 && t.Single(m => m.Key.Hash == root).Withdrawn);
            ThreadReply thread = await CallAsync(mesh, node, new ThreadRequest { Message = second, Limit = 10 });
            Assert.Equal((root, true, string.Empty), (thread.Root.Key.Hash, thread.Root.Withdrawn, thread.Root.Text));
            Assert.Equal([reply, second], thread.Replies.Select(r => r.Key.Hash));
            Assert.Equal(reply, thread.Replies[1].ReplyTo);
        }

        CliResult shown = await CliOkAsync(mesh, Bob, $"chat thread {reply.Value.ToHex()}");
        Assert.Contains("(withdrawn)", shown.Stdout, StringComparison.Ordinal);
        Assert.Contains("eight it is", shown.Stdout, StringComparison.Ordinal);
    }

    // 12. Unfollowed, a Channel keeps its rows; dropped (no peer lists it any more), it leaves none; followed again, its
    // timeline is rebuilt.
    [Fact]
    public async Task AnUnfollowedChannelKeepsItsRows_ADroppedOneLeavesNone_AndFollowingAgainRebuildsIt()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh);
        RunningService aliceChat = await RunChatAsync(mesh, Alice);
        await using RunningService bobChat = await RunChatAsync(mesh, Bob);
        await SendAsync(mesh, Alice, channel, "one");
        await SendAsync(mesh, Alice, channel, "two");
        await mesh.SettleAsync(None);
        await TimelineWhenAsync(mesh, Bob, channel, t => t.Count == 2);

        await UnfollowAsync(mesh, Bob, channel);
        await UntilAsync(mesh, Bob, "the Channel unfollowed, its rows kept", async app => (await ChannelsAsync(app)).SingleOrDefault() is { Followed: false, Messages: 2 });
        await FollowAsync(mesh, Bob, channel);
        await UntilAsync(mesh, Bob, "the Channel followed again", async app => (await ChannelsAsync(app)).SingleOrDefault() is { Followed: true });
        await aliceChat.DisposeAsync();
        await mesh.StopNodeAsync(Alice, None);
        await UnfollowAsync(mesh, Bob, channel);
        await UntilAsync(mesh, Bob, "the Channel dropped", async app => (await ChannelsAsync(app)).Count == 0);
        LocalResult<TimelineReply> gone = await TryCallAsync(mesh, Bob, new TimelineRequest { PackageId = channel, Limit = 10 });

        await mesh.StartNodeAsync(Alice, None);
        await mesh.ConnectAsync(Alice, Bob, None);
        await mesh.SettleAsync(None);
        await FollowAsync(mesh, Bob, channel);
        await mesh.SettleAsync(None);

        Assert.Equal((Status)(ushort)ChatStatus.NotAChannel, gone.Status);
        IReadOnlyList<MessageView> rebuilt = await TimelineWhenAsync(mesh, Bob, channel, t => t.Count == 2);
        Assert.Equal(["one", "two"], rebuilt.Select(m => m.Text));
    }

    // 13. `chat show --json` prints the same document on both devices: what every device agrees on, withdrawals,
    // attachments and replies included.
    [Fact]
    public async Task ChatShowJsonIsEqualOnBothDevices()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        PackageId channel = await SharedChannelAsync(mesh);
        await using RunningService aliceChat = await RunChatAsync(mesh, Alice);
        await using RunningService bobChat = await RunChatAsync(mesh, Bob);
        using var files = new TempFolder();
        string note = files.File("note.txt", 1_234, 3);
        ObjectHash first = await SendByCliAsync(mesh, Alice, $"chat send {Hex(channel)} --text 'with a note' --file '{note}'");
        ObjectHash second = await SendAsync(mesh, Bob, channel, "to be withdrawn");
        await mesh.SettleAsync(None);
        await TimelineWhenAsync(mesh, Bob, channel, t => t.Count == 2);
        await CallAsync(mesh, Bob, new SendRequest { PackageId = channel, Text = "a reply", ReplyTo = first });
        await CallAsync(mesh, Alice, new WithdrawRequest { Message = second });
        await mesh.SettleAsync(None);

        foreach (string node in new[] { Alice, Bob })
            await TimelineWhenAsync(mesh, node, channel, t => t.Count == 3 && t.Any(m => m.Withdrawn));
        CliResult atAlice = await CliOkAsync(mesh, Alice, $"chat show {Hex(channel)} --json --limit 100");
        CliResult atBob = await CliOkAsync(mesh, Bob, $"chat show {Hex(channel)} --json --limit 100");

        Assert.Equal(atAlice.Stdout, atBob.Stdout);
        Assert.Contains("\"withdrawn\": true", atAlice.Stdout, StringComparison.Ordinal);
        Assert.Contains("note.txt", atAlice.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("to be withdrawn", atAlice.Stdout, StringComparison.Ordinal);
    }

    private static DeviceId DeviceOf(string node) => TestIdentity.Of(node).DeviceId;

    private static UserId UserOf(string node) => TestIdentity.Of(node).UserId;

    private static string Hex(PackageId package) => package.Value.ToHex();

    private static T Ok<T>(LocalResult<T> result, string what)
    {
        Assert.True(result.IsOk, $"{what} was refused with {result.Status}: {result.Error?.Message}");
        return result.Value;
    }

    private static Task<RunningService> RunChatAsync(IMeshFixture mesh, string node) => mesh.RunServiceAsync(node, new ChatService(mesh.Clock), None);

    // Alice creates the Channel and grants Bob Write; Bob follows it once Alice's listing reached him.
    private static async Task<PackageId> SharedChannelAsync(IMeshFixture mesh, bool followByBob = true)
    {
        await mesh.PairAsync(Alice, Bob, None);
        await mesh.ConnectAsync(Alice, Bob, None);
        PackageId channel = await CreateAsync(mesh, Alice, "Family");
        await using (LocalClient owner = await mesh.ConnectUserAsync(Alice, "chat-scenario", None))
            Ok(await owner.SendAsync(new SetGrant { PackageId = channel, GranteeId = UserOf(Bob), Rights = GrantRights.Write }, None), "SetGrant");
        await mesh.SettleAsync(None);
        if (followByBob)
        {
            await FollowAsync(mesh, Bob, channel);
            await mesh.SettleAsync(None);
        }

        return channel;
    }

    private static async Task<PackageId> CreateAsync(IMeshFixture mesh, string node, string name)
    {
        await mesh.HoldUserKeyAsync(node, None);
        await using LocalClient client = await mesh.ConnectUserAsync(node, "chat-scenario", None);
        return Ok(await client.SendAsync(new CreatePackage { Kind = PackageKind.Channel, Name = name }, None), "CreatePackage").PackageId;
    }

    private static async Task FollowAsync(IMeshFixture mesh, string node, PackageId package)
    {
        await using LocalClient client = await mesh.ConnectUserAsync(node, "chat-scenario", None);
        Ok(await client.SendAsync(new FollowPackage { PackageId = package }, None), "FollowPackage");
    }

    private static async Task UnfollowAsync(IMeshFixture mesh, string node, PackageId package)
    {
        await using LocalClient client = await mesh.ConnectUserAsync(node, "chat-scenario", None);
        Ok(await client.SendAsync(new UnfollowPackage { PackageId = package }, None), "UnfollowPackage");
    }

    private static async Task<LocalResult<TReply>> TryCallAsync<TRequest, TReply>(IMeshFixture mesh, string node, IChatMethod<TRequest, TReply> request)
        where TRequest : class, IChatMethod<TRequest, TReply>
        where TReply : class, IWireRecord<TReply>
    {
        await using LocalClient app = await mesh.ConnectUserAsync(node, "chat-scenario", None);
        return await ChatCall.SendAsync(app, request, None);
    }

    private static async Task<TReply> CallAsync<TRequest, TReply>(IMeshFixture mesh, string node, IChatMethod<TRequest, TReply> request)
        where TRequest : class, IChatMethod<TRequest, TReply>
        where TReply : class, IWireRecord<TReply> =>
        Ok(await TryCallAsync(mesh, node, request), $"{TRequest.Method} on {node}");

    private static async Task<ObjectHash> SendAsync(IMeshFixture mesh, string node, PackageId channel, string text) =>
        (await CallAsync(mesh, node, new SendRequest { PackageId = channel, Text = text })).Key.Hash;

    private static async Task<CliResult> CliOkAsync(IMeshFixture mesh, string node, string commandLine)
    {
        CliResult result = await mesh.CliAsync(node, commandLine, None);
        Assert.True((int)result.ExitCode == CliOk, $"axiom-mesh {commandLine} on {node}: {result}");
        return result;
    }

    // A message written by `chat send`: the Entry's hash from what it printed.
    private static async Task<ObjectHash> SendByCliAsync(IMeshFixture mesh, string node, string commandLine)
    {
        CliResult written = await CliOkAsync(mesh, node, commandLine);
        Match entry = Regex.Match(written.Stdout, "entry ([0-9a-f]{64})");
        Assert.True(entry.Success && Hash32.TryParseHex(entry.Groups[1].Value, out _), written.ToString());
        return new ObjectHash(Hash32.TryParseHex(entry.Groups[1].Value, out Hash32 hash) ? hash : default);
    }

    private static async Task<IReadOnlyList<ChannelView>> ChannelsAsync(LocalClient app) =>
        Ok(await ChatCall.SendAsync(app, new ChannelsRequest(), None), "channels").Channels;

    private static async Task<IReadOnlyList<MessageView>> TimelineAsync(LocalClient app, PackageId channel)
    {
        var messages = new List<MessageView>();
        EntryKey? after = null;
        while (true)
        {
            LocalResult<TimelineReply> page = await ChatCall.SendAsync(app, new TimelineRequest { PackageId = channel, After = after, Limit = ChatLimits.TimelinePage }, None);
            if (!page.IsOk)
                return messages;
            messages.AddRange(page.Value.Messages);
            if (!page.Value.More || page.Value.Messages.Count == 0)
                return messages;
            after = page.Value.Messages[^1].Key;
        }
    }

    private static async Task<IReadOnlyList<EntryKey>> PagedKeysAsync(IMeshFixture mesh, string node, PackageId channel, bool reverse, ushort limit)
    {
        await using LocalClient app = await mesh.ConnectUserAsync(node, "chat-scenario", None);
        var keys = new List<EntryKey>();
        EntryKey? after = null;
        while (true)
        {
            TimelineReply page = Ok(await ChatCall.SendAsync(app, new TimelineRequest { PackageId = channel, After = after, Reverse = reverse, Limit = limit }, None), "timeline");
            keys.AddRange(page.Messages.Select(m => m.Key));
            if (!page.More)
                return keys;
            after = page.Messages[^1].Key;
        }
    }

    // The timeline once the service caught up with everything the node stores and the condition holds.
    private static async Task<IReadOnlyList<MessageView>> TimelineWhenAsync(IMeshFixture mesh, string node, PackageId channel, Func<IReadOnlyList<MessageView>, bool> condition)
    {
        IReadOnlyList<MessageView> timeline = [];
        await UntilAsync(mesh, node, "the timeline as expected", async app =>
        {
            timeline = await TimelineAsync(app, channel);
            return condition(timeline);
        });
        return timeline;
    }

    // Asks the service until it caught up (LOCAL-SPEC.md §9.3, about.CaughtUp) and the condition holds, within the mesh's deadline.
    private static async Task UntilAsync(IMeshFixture mesh, string node, string what, Func<LocalClient, Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        await using LocalClient app = await mesh.ConnectUserAsync(node, "chat-scenario", None);
        while (true)
        {
            LocalResult<AboutReply> about = await ChatCall.SendAsync(app, new AboutRequest(), None);
            if (about.IsOk && about.Value.CaughtUp && await condition(app))
                return;
            if (deadline.IsCancellationRequested)
                throw new TestMeshException($"The chat service on {node} did not show {what} within {TestMesh.Deadline.TotalSeconds:0} s (about: {about}).", "Check that the node stores the Channel's Entries and that the service is connected.");
            await Task.Yield();
        }
    }

    // Files and folders for attachments, deleted at the end.
    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "axiom-mesh-chat-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name, int size, byte seed)
        {
            string path = System.IO.Path.Combine(Path, name);
            byte[] content = new byte[size];
            for (int i = 0; i < size; i++)
                content[i] = (byte)((i * 31) + seed + (i >> 16));
            System.IO.File.WriteAllBytes(path, content);
            return path;
        }

        public string Folder(string name, params (string Path, int Size)[] files)
        {
            string folder = System.IO.Path.Combine(Path, name);
            byte seed = 1;
            foreach ((string relative, int size) in files)
            {
                string path = System.IO.Path.Combine(folder, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                byte[] content = new byte[size];
                for (int i = 0; i < size; i++)
                    content[i] = (byte)((i * 17) + seed + (i >> 16));
                System.IO.File.WriteAllBytes(path, content);
                seed++;
            }

            return folder;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file still held by a closing handle; the system's temporary folder is cleaned eventually.
            }
        }
    }
}
