using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Chat.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// The chat service end to end on one in-memory node (CHAT-SERVICE.md §6, §8): what it writes is pushed as an event to the
/// apps subscribed; the pins of an attachment no message named are released at its next start; after a restart it answers
/// at once, its index intact.
/// </summary>
public sealed class ChatServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §6.1, §6.2: a message sent is pushed as a message event with its preview, a withdrawal as a withdrawn event, to an app
    // subscribed to the Channel.
    [Fact]
    public async Task WhatTheServiceWritesIsPushedToTheAppsSubscribed()
    {
        await using ChatMeshes chat = await ChatMeshes.StartAsync();
        PackageId channel = await chat.ChannelAsync("Family");
        await using Subscription<ServiceEvent> events = (await chat.App.SubscribeAsync<ServiceEvent>(ChatEventReader.FilterOf(channel), None)).Value;

        SendReply sent = await chat.CallAsync(new SendRequest { PackageId = channel, Text = "pushed" });
        await chat.CallAsync(new WithdrawRequest { Message = sent.Key.Hash });

        MessageEvent message = await NextAsync<MessageEvent>(events);
        WithdrawnEvent withdrawn = await NextAsync<WithdrawnEvent>(events);
        Assert.Equal((sent.Key, "pushed", channel), (message.Key, message.Preview, message.PackageId));
        Assert.Equal(sent.Key, withdrawn.Key);
        Assert.Equal(TestIdentity.Of(ChatMeshes.Node).DeviceId, withdrawn.By);
    }

    // §8, LOCAL-SPEC.md §9.5, §9.6: an attach whose send never came leaves the service's pins; the next start releases them;
    // the index is kept across the restart.
    [Fact]
    public async Task ThePinsOfAnAttachmentNoMessageNamedAreReleasedAtTheNextStart_AndTheIndexIsKept()
    {
        await using ChatMeshes chat = await ChatMeshes.StartAsync();
        PackageId channel = await chat.ChannelAsync("Family");
        await chat.CallAsync(new SendRequest { PackageId = channel, Text = "kept" });
        await using (Subscription<ServiceEvent> events = (await chat.App.SubscribeAsync<ServiceEvent>(ChatEventReader.FilterOf(default), None)).Value)
        {
            AttachReply started = await chat.CallAsync(new AttachRequest { Path = ChatMeshes.TempFile("unsent.bin", 2_000) });
            Assert.Equal(JobState.Done, (await ChatMeshes.JobEndAsync(events, started.Job)).State);
        }

        ObjectHash[] before = await chat.ServicePinsAsync();
        await chat.RestartAsync();

        Assert.Equal(2, before.Length);
        Assert.Empty(await chat.ServicePinsAsync());
        Assert.Equal(["kept"], (await chat.CallAsync(new TimelineRequest { PackageId = channel, Limit = 10 })).Messages.Select(m => m.Text));
    }

    // §5.7, LOCAL-SPEC.md §9.1: a service spawned before identity create decides by the identity as it stands, not by the
    // empty one its HelloAck carried: the owner's own device withdraws a guest's message without the service restarting.
    [Fact]
    public async Task AServiceRunningBeforeIdentityCreateWithdrawsAGuestsMessageAfterwards_AsTheOwnersDevice()
    {
        const string bob = "bob-desktop";
        await using TestMesh mesh = await TestMesh.StartAsync(
            TestMeshOptions.Of(ChatMeshes.Node, bob) with { Local = TestServices.With(TestCliService.Local, ChatProgram.Definition), WithoutIdentity = [ChatMeshes.Node] },
            None);
        await using RunningService chat = await mesh[ChatMeshes.Node].RunServiceAsync(new ChatService(mesh.Clock), None);
        Identity created = await mesh.CreateIdentityAsync(ChatMeshes.Node, "Alice", "laptop", None);
        await mesh.PairAsync(ChatMeshes.Node, bob, None);
        await mesh.ConnectAsync(ChatMeshes.Node, bob, None);
        await using LocalClient app = await mesh[ChatMeshes.Node].ConnectUserAsync("chat-test", None);
        LocalResult<Package> family = await app.SendAsync(new CreatePackage { Kind = PackageKind.Channel, Name = "Family" }, None);
        Assert.True(family.IsOk, family.ToString());
        PackageId channel = family.Value.PackageId;
        LocalResult<Grant> granted = await app.SendAsync(new SetGrant { PackageId = channel, GranteeId = TestIdentity.Of(bob).UserId, Rights = GrantRights.Write }, None);
        Assert.True(granted.IsOk, granted.ToString());
        await mesh.SettleAsync(None);
        await mesh[bob].FollowAsync(channel, None);
        await mesh.SettleAsync(None);
        ObjectHash byBob = await mesh[bob].WriteEntryAsync(channel, new Payloads.MessagePayload { Text = "from a guest", Attachments = [], ReplyTo = default }, None);
        await mesh.SettleAsync(None);
        await TimelineWhenAsync(app, channel, t => t.Count == 1);

        LocalResult<WithdrawReply> withdrawn = await ChatCall.SendAsync(app, new WithdrawRequest { Message = byBob }, None);
        IReadOnlyList<MessageView> shown = await TimelineWhenAsync(app, channel, t => t.Count == 1 && t[0].Withdrawn);

        Assert.True(withdrawn.IsOk, withdrawn.ToString());
        Assert.Equal((byBob, true, string.Empty, created.DeviceId), (shown[0].Key.Hash, shown[0].Withdrawn, shown[0].Text, shown[0].WithdrawnBy));
    }

    private static async Task<IReadOnlyList<MessageView>> TimelineWhenAsync(LocalClient app, PackageId channel, Func<IReadOnlyList<MessageView>, bool> condition)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        while (true)
        {
            LocalResult<AboutReply> about = await ChatCall.SendAsync(app, new AboutRequest(), None);
            LocalResult<TimelineReply> timeline = await ChatCall.SendAsync(app, new TimelineRequest { PackageId = channel, Limit = 10 }, None);
            if (about.IsOk && about.Value.CaughtUp && timeline.IsOk && condition(timeline.Value.Messages))
                return timeline.Value.Messages;
            if (deadline.IsCancellationRequested)
                throw new TestMeshException($"The timeline did not reach the expected state within {TestMesh.Deadline.TotalSeconds:0} s (about: {about}, timeline: {timeline}).", "Check that the node stores the Channel's Entries and that the service is connected.");
            await Pause.ShortAsync();
        }
    }

    private static async Task<T> NextAsync<T>(Subscription<ServiceEvent> events)
        where T : ChatEvent
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        await foreach (ServiceEvent received in events.WithCancellation(deadline.Token))
        {
            if (ChatEventReader.TryRead(received, out ChatEvent? chat) && chat is T wanted)
                return wanted;
        }

        throw new TestMeshException($"No {typeof(T).Name} arrived.", "Check that the service emits for the Channel.");
    }
}
