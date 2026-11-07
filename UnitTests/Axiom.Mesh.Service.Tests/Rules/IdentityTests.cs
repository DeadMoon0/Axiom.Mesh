using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service.Tests.Support;
using Axiom.Mesh.Testing;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Service.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §9.1, §6.1 and §7.2: a service decides by the Host's identity as it stands, not as the <c>HelloAck</c> of
/// its connection said it. A service the Host spawned at start connects before <c>CreateIdentity</c> and sees the identity
/// once it is created, and every Roster after it (a renamed device), without reconnecting.
/// </summary>
public sealed class IdentityTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §9.1, §6.1, §7.2: before the identity exists the session has none (no zero ids); CreateIdentity reaches the
    // running service as the identity GetIdentity answers; RenameDevice reaches it as the next Roster.
    [Fact]
    public async Task AServiceConnectedBeforeTheIdentityExistsSeesItOnceItIsCreated_AndEveryRosterAfterIt()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(
            TestMeshOptions.Of(ServiceMeshes.Node) with { Local = TestServices.With(TestCliService.Local, TestEchoService.Echo), WithoutIdentity = [ServiceMeshes.Node] },
            None);
        var service = new RecordingService();
        await using RunningService echo = await mesh[ServiceMeshes.Node].RunServiceAsync(service, None);
        ServiceSession session = service.Session!;
        Identity? before = session.Identity;

        Identity created = await mesh.CreateIdentityAsync(ServiceMeshes.Node, "Alice", "laptop", None);
        Identity seen = await IdentityWhenAsync(session, i => i.DeviceId == created.DeviceId);
        LocalClient cli = await mesh[ServiceMeshes.Node].ConnectUserAsync("cli", None);
        Device renamed = (await cli.SendAsync(new RenameDevice { DeviceId = created.DeviceId, Name = "desk" }, None)).Value;
        Identity after = await IdentityWhenAsync(session, i => i.RosterSeq == 2);

        Assert.Null(before);
        Assert.Equal(created, seen);
        Assert.Equal(("desk", "desk", created.UserId, created.DeviceId), (renamed.Name, after.DeviceName, after.UserId, after.DeviceId));
        Assert.False(session.Host.Identity.Present);
    }

    private static async Task<Identity> IdentityWhenAsync(ServiceSession session, Func<Identity, bool> condition)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        while (true)
        {
            if (session.Identity is { } identity && condition(identity))
                return identity;
            if (deadline.IsCancellationRequested)
                throw new TestMeshException($"The session's identity did not reach the expected state within {TestMesh.Deadline.TotalSeconds:0} s (now: {session.Identity?.ToString() ?? "none"}).", "Check that the runtime subscribes to topic users and refreshes with GetIdentity.");
            await Pause.ShortAsync();
        }
    }
}
