using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Service.Tests.Support;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Service.Tests.Regression;

/// <summary>
/// What a session still takes from the <c>HelloAck</c> of its connection (LOCAL-SPEC.md §4.1, §9.3) now that the identity
/// is live: the store epoch it indexes under and the newest store sequence at connect, which a service cuts its live view at.
/// </summary>
public sealed class HelloAckTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // Damage: with the identity made live, the HelloAck went out of the session along with the store epoch and the newest
    // store sequence a service plans its catch-up and its live cut by; a service that read them got defaults.
    [Fact]
    public async Task TheSessionStillCarriesTheHelloAcksStoreEpochAndNewestStoreSequence_AndItsIdentityAtConnect()
    {
        await using TestMesh mesh = await ServiceMeshes.StartAsync();
        PackageId notes = await ServiceMeshes.NotesAsync(mesh);
        await ServiceMeshes.WriteAsync(mesh, notes, 3);
        var service = new RecordingService();
        await using RunningService echo = await mesh[ServiceMeshes.Node].RunServiceAsync(service, None);
        await service.Echo.WaitForCountAsync(3, None);
        ServiceSession session = service.Session!;

        Assert.Equal(session.StoreEpoch, session.Host.StoreEpoch);
        Assert.Equal(service.Echo.Seen.Last().StoreSeq, session.Host.StoreSeq);
        Assert.True(session.Host.Identity.Present);
        Assert.Equal(session.Host.Identity, session.Identity);
    }
}
