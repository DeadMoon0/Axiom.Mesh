using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Chat.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;

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
