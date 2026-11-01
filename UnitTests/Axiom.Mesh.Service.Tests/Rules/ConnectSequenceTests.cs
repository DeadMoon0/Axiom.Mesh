using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service.Tests.Support;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Service.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §9.1 and §9.3, the connect sequence the runtime runs for every service: <c>Hello</c> in the service role with
/// its name and token, <c>RegisterKind</c> for each of its kinds, then every Entry of its kinds once, in store order, from
/// the catch-up and then live, and its Packages' changes.
/// </summary>
public sealed class ConnectSequenceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §9.1, §9.2: the service connects in the service role as its name and holds its kind: ListServices shows it connected
    // with its version, and another registration of the kind is KindTaken.
    [Fact]
    public async Task AServiceConnectsAsItsNameAndHoldsItsKinds()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        await using RunningService echo = await mesh[ServiceMeshes.Node].RunServiceAsync(new TestEchoService(), None);
        LocalClient app = await mesh[ServiceMeshes.Node].ConnectUserAsync("app", None);

        Local.Service listed = (await app.SendAsync(new ListServices(), None)).Value.Single(s => s.Name == "echo");

        Assert.True(listed.Connected);
        Assert.Equal("test", listed.Version);
        Assert.Equal([TestEchoService.Kind], listed.Kinds);
    }

    // §9.3: Entries stored before the service connected and while it runs reach it once each, in store order; Entries of
    // other kinds never do.
    [Fact]
    public async Task EveryEntryOfItsKindsArrivesOnceInStoreOrder_BeforeAndAfterItConnected_AndNoOtherKind()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        PackageId notes = await ServiceMeshes.NotesAsync(mesh);
        PackageId channel = await mesh[ServiceMeshes.Node].CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        var before = await ServiceMeshes.WriteAsync(mesh, notes, 3);
        await mesh[ServiceMeshes.Node].WriteEntryAsync(channel, new Payloads.MessagePayload { Text = "not for echo", Attachments = [], ReplyTo = default }, None);
        var service = new TestEchoService();
        await using RunningService echo = await mesh[ServiceMeshes.Node].RunServiceAsync(service, None);

        var after = await ServiceMeshes.WriteAsync(mesh, notes, 2);
        await service.WaitForCountAsync(5, None);

        Assert.Equal([.. before, .. after], service.Seen.Select(e => e.Hash));
        Assert.True(service.Seen.Zip(service.Seen.Skip(1)).All(p => p.First.StoreSeq < p.Second.StoreSeq));
    }

    // §9.10, §7.2: what the service emits reaches an app subscribed to topic service, for the Package the Entry is in.
    [Fact]
    public async Task WhatTheServiceEmitsReachesAnAppSubscribedToItsEvents()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        PackageId notes = await ServiceMeshes.NotesAsync(mesh);
        LocalClient app = await mesh[ServiceMeshes.Node].ConnectUserAsync("app", None);
        Subscription<ServiceEvent> events = (await app.SubscribeAsync<ServiceEvent>(Filter.Any with { Service = "echo" }, None)).Value;
        await using RunningService echo = await mesh[ServiceMeshes.Node].RunServiceAsync(new TestEchoService(), None);

        ObjectHash written = (await ServiceMeshes.WriteAsync(mesh, notes, 1))[0];

        ServiceEvent seen = await Events.FirstAsync(events, _ => true);
        byte[] hash = new byte[Hash32.Length];
        written.Value.CopyTo(hash);

        Assert.Equal(("echo", "seen", notes), (seen.Service, seen.Event, seen.PackageId));
        Assert.Equal(hash, seen.Payload.ToArray());
    }
}
