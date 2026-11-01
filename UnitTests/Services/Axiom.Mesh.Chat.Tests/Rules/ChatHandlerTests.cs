using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Chat.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// The methods of <c>axiom-mesh-chat/1</c> on a running node (CHAT-SERVICE.md §5, §7, §8): each refuses what is wrong with
/// its status; an attachment is put, the message written, and only then the service's pins released (LOCAL-SPEC.md §9.5);
/// a job reports its start and its end as events.
/// </summary>
public sealed class ChatHandlerTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §5, §7: an unknown Channel is NotAChannel, an unknown message NotAMessage, a bad limit, a relative path, an unknown
    // method and a payload of another layout Invalid.
    [Fact]
    public async Task EveryMethodRefusesWhatIsWrongWithItsStatus()
    {
        await using ChatMeshes chat = await ChatMeshes.StartAsync();
        PackageId unknown = new(Facts.Id(0x99));
        ObjectHash nothing = Facts.Hash(0x98);

        Assert.Equal(Chat(ChatStatus.NotAChannel), (await ChatCall.SendAsync(chat.App, new TimelineRequest { PackageId = unknown, Limit = 10 }, None)).Status);
        Assert.Equal(Chat(ChatStatus.NotAChannel), (await ChatCall.SendAsync(chat.App, new SendRequest { PackageId = unknown, Text = "hi" }, None)).Status);
        Assert.Equal(Chat(ChatStatus.NotAMessage), (await ChatCall.SendAsync(chat.App, new MessageRequest { Hash = nothing }, None)).Status);
        Assert.Equal(Chat(ChatStatus.NotAMessage), (await ChatCall.SendAsync(chat.App, new WithdrawRequest { Message = nothing }, None)).Status);
        Assert.Equal(Chat(ChatStatus.NotAMessage), (await ChatCall.SendAsync(chat.App, new ThreadRequest { Message = nothing, Limit = 5 }, None)).Status);
        Assert.Equal(Chat(ChatStatus.NotAMessage), (await ChatCall.SendAsync(chat.App, new GetRequest { Hash = nothing, Message = nothing, Path = Path.GetTempPath() }, None)).Status);
        PackageId channel = await chat.ChannelAsync("Family");
        Assert.Equal(Status.Invalid, (await ChatCall.SendAsync(chat.App, new TimelineRequest { PackageId = channel, Limit = 0 }, None)).Status);
        Assert.Equal(Status.Invalid, (await ChatCall.SendAsync(chat.App, new AttachRequest { Path = "relative.txt" }, None)).Status);
        LocalResult<ReadOnlyMemory<byte>> unknownMethod = await chat.App.SendAsync(new ServiceCall { Service = ChatProtocol.ServiceName, Method = "dance", Payload = Array.Empty<byte>() }, None);
        LocalResult<ReadOnlyMemory<byte>> wrongLayout = await chat.App.SendAsync(new ServiceCall { Service = ChatProtocol.ServiceName, Method = ChatMethods.Message, Payload = new byte[] { 1, 2, 3 } }, None);
        Assert.Equal((Status.Invalid, "method"), (unknownMethod.Status, unknownMethod.Error!.Detail));
        Assert.Equal((Status.Invalid, "payload"), (wrongLayout.Status, wrongLayout.Error!.Detail));
    }

    // §5.6, §5.8, LOCAL-SPEC.md §9.5: attach puts the file and keeps the service's pins on its objects; the job reports its
    // start and its end; send writes the message, then the pins are released, the Outbound pins holding what no peer has.
    [Fact]
    public async Task AnAttachmentIsPutThenTheMessageWrittenThenThePinsReleased_TheJobReportsStartAndEnd()
    {
        await using ChatMeshes chat = await ChatMeshes.StartAsync();
        PackageId channel = await chat.ChannelAsync("Family");
        string file = ChatMeshes.TempFile("photo.bin", 1_300_000);
        LocalResult<Subscription<ServiceEvent>> subscribed = await chat.App.SubscribeAsync<ServiceEvent>(ChatEventReader.FilterOf(default), None);
        await using Subscription<ServiceEvent> events = subscribed.Value;

        AttachReply started = await chat.CallAsync(new AttachRequest { Path = file, Name = "photo" });
        JobView ended = await ChatMeshes.JobEndAsync(events, started.Job);
        ObjectHash[] pinnedBeforeSend = await chat.ServicePinsAsync();
        SendReply sent = await chat.CallAsync(new SendRequest { PackageId = channel, Text = "look", Attachments = [ended.Attachment!.Value] });
        MessageReply shown = await chat.CallAsync(new MessageRequest { Hash = sent.Key.Hash });

        Assert.Equal((JobKind.Attach, JobState.Done, "photo", 1_300_000ul), (ended.Kind, ended.State, ended.Attachment.Value.Name, ended.Attachment.Value.Size));
        Assert.Equal(3, pinnedBeforeSend.Length);
        Assert.Contains(ended.Attachment.Value.Hash, pinnedBeforeSend);
        Assert.Empty(await chat.ServicePinsAsync());
        Assert.Equal(("look", 1), (shown.Message.Text, shown.Message.Attachments.Count));
        Assert.Contains((await chat.CallAsync(new JobsRequest())).Jobs, j => j.Job == started.Job && j.State == JobState.Done);
    }

    // §5.9, §7: get names an attachment of the message, else AttachmentMissing; a withdrawn message's attachments are not
    // fetched (Withdrawn).
    [Fact]
    public async Task GetRefusesAnAttachmentTheMessageDoesNotName_AndAnyOfAWithdrawnMessage()
    {
        await using ChatMeshes chat = await ChatMeshes.StartAsync();
        PackageId channel = await chat.ChannelAsync("Family");
        SendReply sent = await chat.CallAsync(new SendRequest { PackageId = channel, Text = "no files" });
        string target = Path.Combine(Path.GetTempPath(), "axiom-mesh-chat-tests", Guid.NewGuid().ToString("N"));

        LocalResult<GetReply> missing = await ChatCall.SendAsync(chat.App, new GetRequest { Hash = Facts.Hash(0x31), Message = sent.Key.Hash, Path = target, Existing = ExistingPath.Fail }, None);
        await chat.CallAsync(new WithdrawRequest { Message = sent.Key.Hash });
        LocalResult<GetReply> withdrawn = await ChatCall.SendAsync(chat.App, new GetRequest { Hash = Facts.Hash(0x31), Message = sent.Key.Hash, Path = target, Existing = ExistingPath.Fail }, None);

        Assert.Equal(Chat(ChatStatus.AttachmentMissing), missing.Status);
        Assert.Equal(Chat(ChatStatus.Withdrawn), withdrawn.Status);
    }

    private static Status Chat(ChatStatus status) => (Status)(ushort)status;
}
