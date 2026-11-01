using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §9.10, <c>Emit</c>: a service's event reaches every subscription of topic <c>service</c> whose filter takes
/// that service and the Package; the zero Package is always allowed; a Package that is not followed is <c>NotFound</c>, one
/// of a kind the service did not register <c>KindNotRegistered</c>.
/// </summary>
public sealed class EmitTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §9.10, §7.2: an event about no Package reaches the subscriptions naming the service or no service, with the
    // service's name, the event's name and its payload as emitted.
    [Fact]
    public async Task AnEventAboutNoPackageReachesEverySubscriptionThatTakesTheService()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient app = await node.ConnectUserAsync("app", None);
        Subscription<ServiceEvent> any = (await app.SubscribeAsync<ServiceEvent>(Filter.Any, None)).Value;
        Subscription<ServiceEvent> cli = (await app.SubscribeAsync<ServiceEvent>(Filter.Any with { Service = "cli" }, None)).Value;
        LocalClient service = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);

        Assert.True((await service.SendAsync(new Emit { Event = "seen", PackageId = default, Payload = new byte[] { 4, 2 } }, None)).IsOk);

        ServiceEvent first = await Events.FirstAsync(any, _ => true);
        ServiceEvent second = await Events.FirstAsync(cli, _ => true);
        Assert.Equal(("cli", "seen"), (first.Service, first.Event));
        Assert.Equal(new byte[] { 4, 2 }, first.Payload.ToArray());
        Assert.Equal(("cli", "seen"), (second.Service, second.Event));
    }

    // §9.10: an event about a followed Package of a kind the service registered reaches the subscriptions of that Package.
    [Fact]
    public async Task AnEventAboutAFollowedPackageOfARegisteredKindReachesTheSubscriptionsOfThatPackage()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        PackageId channel = await node.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        LocalClient app = await node.ConnectUserAsync("app", None);
        Subscription<ServiceEvent> family = (await app.SubscribeAsync<ServiceEvent>(Filter.Any with { PackageId = channel }, None)).Value;
        LocalClient service = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        Assert.True((await service.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message, EntryKind.Redaction] }, None)).IsOk);

        Assert.True((await service.SendAsync(new Emit { Event = "timeline", PackageId = channel }, None)).IsOk);

        ServiceEvent seen = await Events.FirstAsync(family, _ => true);
        Assert.Equal(("timeline", channel), (seen.Event, seen.PackageId));
    }

    // §9.10, §3.2: a Package that is not followed is NotFound; a followed one of a kind the service did not register is
    // KindNotRegistered; an event name outside the name characters is Invalid.
    [Fact]
    public async Task AnUnknownPackageIsNotFound_AKindNotRegisteredIsKindNotRegistered_AndABadNameIsInvalid()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        PackageId archive = await node.CreatePackageAsync("Photos", PackageKind.Archive, Retention.Default, None);
        PackageId unknown = PackageId.Derive(TestIdentity.Of("bob-desktop").UserId, new PackageNonce(Hash32.Of([7])));
        LocalClient service = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);

        Assert.Equal(Status.NotFound, (await service.SendAsync(new Emit { Event = "seen", PackageId = unknown }, None)).Status);
        Assert.Equal(Status.KindNotRegistered, (await service.SendAsync(new Emit { Event = "seen", PackageId = archive }, None)).Status);
        Assert.Equal(Status.Invalid, (await service.SendAsync(new Emit { Event = "not a name", PackageId = default }, None)).Status);
    }
}
