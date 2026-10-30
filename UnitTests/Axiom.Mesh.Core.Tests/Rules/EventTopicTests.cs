using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §7.2 and §7.3, the topics M9 fills: <c>transfers</c> reports an object wanted and stored on the side that
/// receives it and sent on the side that serves it, filtered by Package and peer; <c>service</c> carries a service's
/// events to subscriptions naming that service; and the telemetry requests answer <c>NotFound</c> on a Host without
/// telemetry (§14.4 bit 2).
/// </summary>
public sealed class EventTopicTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §5.16, §7.2: Bob's node wants Alice's Entry and stores it; Alice's node sends it. Each side's transfers subscription
    // filtered to the other device sees its steps.
    [Fact]
    public async Task AnEntryWantedAndStoredOnOneSideIsSentOnTheOther_OnTopicTransfers()
    {
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        PackageId channel = await alice.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await alice.GrantAsync(channel, "bob", GrantRights.Read, None);
        ObjectHash entry = await alice.WriteEntryAsync(channel, ObjectFixtures.Message("hello"), None);
        await bob.FollowAsync(channel, None);
        LocalClient atBob = await bob.ConnectUserAsync("app", None);
        LocalClient atAlice = await alice.ConnectUserAsync("app", None);
        Subscription<TransferChanged> incoming = (await atBob.SubscribeAsync<TransferChanged>(Filter.Any with { DeviceId = alice.Device }, None)).Value;
        Subscription<TransferChanged> outgoing = (await atAlice.SubscribeAsync<TransferChanged>(Filter.Any with { DeviceId = bob.Device }, None)).Value;

        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);

        Transfer wanted = (await Events.FirstAsync(incoming, e => e.Transfer.Hash == entry && e.Transfer.State == TransferState.Wanted)).Transfer;
        Transfer stored = (await Events.FirstAsync(incoming, e => e.Transfer.Hash == entry && e.Transfer.State == TransferState.Stored)).Transfer;
        Transfer sent = (await Events.FirstAsync(outgoing, e => e.Transfer.Hash == entry && e.Transfer.State == TransferState.Sent)).Transfer;
        Assert.Equal((alice.Device, channel, TransferDirection.In), (wanted.Peer, wanted.PackageId, wanted.Direction));
        Assert.Equal((alice.Device, TransferDirection.In), (stored.Peer, stored.Direction));
        Assert.True(stored.Size > 0);
        Assert.Equal((bob.Device, TransferDirection.Out, stored.Size), (sent.Peer, sent.Direction, sent.Size));
    }

    // §7.2, §7.3: a ServiceEvent reaches the subscriptions whose filter names its service, or no service, and not others.
    [Fact]
    public async Task AServiceEventReachesTheSubscriptionsNamingItsServiceOnly()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient client = await node.ConnectUserAsync("app", None);
        Subscription<ServiceEvent> cli = (await client.SubscribeAsync<ServiceEvent>(Filter.Any with { Service = "cli" }, None)).Value;
        Subscription<ServiceEvent> sync = (await client.SubscribeAsync<ServiceEvent>(Filter.Any with { Service = "sync" }, None)).Value;

        node.Node.Local.Hub.ServiceEmitted(new ServiceEvent { Service = "cli", Event = "posted" });
        node.Node.Local.Hub.ServiceEmitted(new ServiceEvent { Service = "sync", Event = "synced" });

        Assert.Equal("posted", (await Events.FirstAsync(cli, _ => true)).Event);
        Assert.Equal("synced", (await Events.FirstAsync(sync, _ => true)).Event);
    }

    // §10.3, §14.4: a Host without telemetry answers every telemetry request NotFound and clears capability bit 2.
    [Fact]
    public async Task TelemetryRequestsAreNotFoundOnAHostWithoutTelemetry()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        LocalClient client = await mesh[LocalMeshes.Node].ConnectUserAsync("app", None);

        Assert.False(client.Host.Capabilities.HasFlag(HostCapabilities.Telemetry));
        Assert.Equal(Status.NotFound, (await client.SendAsync(new ListSeries(), None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new ListKeys { Series = "store.bytes" }, None)).Status);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new GetTotal { Series = "store.bytes", Key = "" }, None)).Status);
    }
}
