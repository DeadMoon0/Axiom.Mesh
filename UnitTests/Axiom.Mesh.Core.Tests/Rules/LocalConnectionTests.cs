using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §2.3, §3.1, §3.4 and §11.3 against a running node: what a connection owns ends with it; an unknown
/// request is an error response, not a closed connection; correlation misuse and a frame not completed in time are
/// violations, answered <c>Bye(Violation)</c>.
/// </summary>
public sealed class LocalConnectionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §2.3: when a connection ends, its subscriptions are gone, its operations cancelled, and a service's name is free for
    // the next connection. (No callback is issued before M9, so none is outstanding to abandon.)
    [Fact]
    public async Task WhenAConnectionEndsItsSubscriptionsOperationsCallbacksAndRegistrationsEndWithIt()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        var operations = new HeldOperations();
        operations.Install(node.Node.Local);

        LocalClient cli = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        Assert.True((await cli.SubscribeAsync<EntryStored>(Filter.Any, None)).IsOk);
        Assert.True((await cli.SendAsync(new Fetch { PackageId = default, Hash = default }, None)).IsOk);
        LocalConnection held = Assert.Single(node.Node.Local.Book.Live());
        Assert.Equal(1, held.Outbox.Subscriptions);
        Assert.Equal(1, held.Operations.Running);

        await cli.ByeAsync(None);
        await Waits.UntilAsync(() => node.Node.Local.Connections == 0, "the connection's end");

        Assert.Equal(0, held.Outbox.Subscriptions);
        Assert.Equal(0, held.Operations.Running);
        await Waits.UntilAsync(() => operations.Cancelled == 1, "the operation's work to see its cancellation");
        LocalClient again = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        Assert.Equal(Role.Service, again.Host.Role);
    }

    // §1.5, §3.4: an id the protocol knows but this Host does not serve, and an id the protocol does not know, are both
    // answered UnknownRequest; the connection goes on.
    [Fact]
    public async Task AnUnknownRequestIdIsAnsweredUnknownRequestAndTheConnectionStaysOpen()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        await using RawLocalClient client = await RawLocalClient.ReadyAsync(mesh, LocalMeshes.Node);

        uint unserved = await client.RequestAsync(new UpdatePackage { PackageId = default });
        Assert.Equal(Status.UnknownRequest, (await client.ResponseAsync(unserved)).Status);

        await client.WriteRawAsync(RawLocalClient.RequestBytes(300, 77));
        Assert.Equal(Status.UnknownRequest, (await client.ResponseAsync(77)).Status);

        uint identity = await client.RequestAsync(new GetIdentity());
        Assert.Equal(Status.Ok, (await client.ResponseAsync(identity)).Status);
    }

    // §3.4, §11.3: a correlation of 0, one whose response was not yet sent, and a 65th outstanding request are violations.
    [Fact]
    public async Task ACorrelationOf0OrStillOutstanding_OrA65thOutstandingRequest_IsAViolation()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LocalPort port = mesh[LocalMeshes.Node].Node.Local;
        port.Routes = port.Routes.With(GetStore.Id, RequestRoutes.Of<GetStore>(async (context, correlation, request, cancellationToken) =>
        {
            await gate.Task.WaitAsync(cancellationToken);
            return LocalAnswer.Ok<GetStore, Store>(correlation, new Store());
        }));

        await using (RawLocalClient zero = await RawLocalClient.ReadyAsync(mesh, LocalMeshes.Node))
        {
            await zero.WriteRawAsync(RawLocalClient.RequestBytes(GetIdentity.Id, 0));
            Assert.Equal(ByeReason.Violation, (await zero.EndAsync())?.Reason);
        }

        await using (RawLocalClient twice = await RawLocalClient.ReadyAsync(mesh, LocalMeshes.Node))
        {
            await twice.SendAsync(new RequestFrame(5, new GetStore()));
            await twice.SendAsync(new RequestFrame(5, new GetStore()));
            Assert.Equal(ByeReason.Violation, (await twice.EndAsync())?.Reason);
        }

        await using (RawLocalClient many = await RawLocalClient.ReadyAsync(mesh, LocalMeshes.Node))
        {
            for (uint correlation = 1; correlation <= Limits.MaxOutstandingRequests; correlation++)
                await many.SendAsync(new RequestFrame(correlation, new GetStore()));
            await many.WriteRawAsync(RawLocalClient.RequestBytes(GetStore.Id, 65));
            Assert.Equal(ByeReason.Violation, (await many.EndAsync())?.Reason);
        }

        gate.TrySetResult();
    }

    // §3.1: a frame must be received completely within 30 seconds of its first byte; the Host closes with Bye(Violation).
    [Fact]
    public async Task AFrameNotCompletedWithin30SecondsClosesTheLocalConnection()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        await using RawLocalClient client = await RawLocalClient.ReadyAsync(mesh, LocalMeshes.Node);

        LocalConnection connection = Assert.Single(mesh[LocalMeshes.Node].Node.Local.Book.Live());
        mesh.Clock.Advance(TimeSpan.FromSeconds(1));
        byte[] half = new byte[4 + 10];
        BinaryPrimitives.WriteUInt32BigEndian(half, 100);
        await client.WriteRawAsync(half);
        await Waits.UntilAsync(() => connection.SilentFor == TimeSpan.Zero, "the first bytes of the frame to arrive");
        mesh.Clock.Advance(TimeSpan.FromSeconds(Limits.FrameCompletionSeconds - 1));
        Assert.Equal(1, mesh[LocalMeshes.Node].Node.Local.Connections);
        mesh.Clock.Advance(TimeSpan.FromSeconds(1));

        Bye? bye = await client.EndAsync();
        Assert.Equal(ByeReason.Violation, bye?.Reason);
    }
}
