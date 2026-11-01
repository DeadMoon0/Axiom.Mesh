using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Chat.Tests.Support;

/// <summary>One in-memory node with the chat service configured and running beside it, and a user client to call it.</summary>
internal sealed class ChatMeshes : IAsyncDisposable
{
    public const string Node = "alice-laptop";

    private static readonly CancellationToken None = CancellationToken.None;

    private ChatMeshes(TestMesh mesh, RunningService chat, LocalClient app)
    {
        Mesh = mesh;
        Chat = chat;
        App = app;
    }

    public TestMesh Mesh { get; }

    public RunningService Chat { get; private set; }

    public LocalClient App { get; }

    public static async Task<ChatMeshes> StartAsync()
    {
        TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of(Node) with { Local = TestServices.With(TestCliService.Local, ChatProgram.Definition) }, None);
        RunningService chat = await mesh[Node].RunServiceAsync(new ChatService(mesh.Clock), None);
        LocalClient app = await mesh[Node].ConnectUserAsync("chat-test", None);
        return new ChatMeshes(mesh, chat, app);
    }

    /// <summary>A Channel Alice owns, followed here.</summary>
    public async Task<PackageId> ChannelAsync(string name)
    {
        await ((IMeshFixture)Mesh).HoldUserKeyAsync(Node, None);
        LocalResult<Package> created = await App.SendAsync(new CreatePackage { Kind = PackageKind.Channel, Name = name }, None);
        Assert.True(created.IsOk, created.ToString());
        return created.Value.PackageId;
    }

    /// <summary>Stops the service and starts a new instance on the same state, as a restart does.</summary>
    public async Task RestartAsync()
    {
        await Chat.DisposeAsync();
        Chat = await Mesh[Node].RunServiceAsync(new ChatService(Mesh.Clock), None);
    }

    public async Task<TReply> CallAsync<TRequest, TReply>(IChatMethod<TRequest, TReply> request)
        where TRequest : class, IChatMethod<TRequest, TReply>
        where TReply : class, IWireRecord<TReply>
    {
        LocalResult<TReply> result = await ChatCall.SendAsync(App, request, None);
        Assert.True(result.IsOk, $"{TRequest.Method}: {result}");
        return result.Value;
    }

    /// <summary>The pins with reason Service the chat service holds on the node.</summary>
    public async Task<ObjectHash[]> ServicePinsAsync()
    {
        LocalResult<PinPage> pins = await App.SendAsync(new ListPins(), None);
        Assert.True(pins.IsOk, pins.ToString());
        return [.. pins.Value.Pins.Where(p => p.Reason == PinReason.Service).Select(p => p.Hash).Order()];
    }

    /// <summary>Waits for the job's end on the subscription made before it started.</summary>
    public static async Task<JobView> JobEndAsync(Subscription<ServiceEvent> events, uint job)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        await foreach (ServiceEvent received in events.WithCancellation(deadline.Token))
        {
            if (ChatEventReader.TryRead(received, out ChatEvent? chat) && chat is JobEvent { Job: { State: not JobState.Running } ended } && ended.Job == job)
                return ended;
        }

        throw new InvalidOperationException("The subscription ended before the job did.");
    }

    public static string TempFile(string name, int size)
    {
        string folder = Path.Combine(Path.GetTempPath(), "axiom-mesh-chat-tests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, [.. Enumerable.Range(0, size).Select(i => (byte)(i * 7))]);
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await Chat.DisposeAsync();
        await Mesh.DisposeAsync();
    }
}
