using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;
using LocalPinReason = Axiom.Mesh.Local.PinReason;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §6.5 and §5.11, pins and the store: the user role pins with reason User and source zero, the service
/// role with reason Service and source H(service name); <c>Pin</c> is idempotent and <c>NotFound</c> for an object not
/// stored; <c>Unpin</c> counts what it removed and touches no other pin; <c>ReleasePins</c> is <c>Invalid</c> for the
/// Host's reasons; pins list ascending by (hash, reason, source); a local write past the budget is <c>StoreFull</c>.
/// </summary>
public sealed class StoreRequestTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §6.5: Pin in the user role is reason User, source zero, idempotent; an object not stored is NotFound; Unpin
    // counts 1, then 0.
    [Fact]
    public async Task AUserPinIsReasonUserSourceZero_Idempotent_AndUnpinCountsWhatItRemoved()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient client = await node.ConnectUserAsync("app", None);
        Chunk chunk = Samples.Chunk(1);
        Assert.True(await node.Node.PutAsync(chunk, None));

        Pin pin = (await client.SendAsync(new PinObject { Hash = chunk.Hash }, None)).Value;
        Pin again = (await client.SendAsync(new PinObject { Hash = chunk.Hash }, None)).Value;

        Assert.Equal((chunk.Hash, LocalPinReason.User, default(Hash32), (ulong)chunk.Bytes.Length), (pin.Hash, pin.Reason, pin.Source, pin.Size));
        Assert.Equal(pin, again);
        Assert.Single((await client.SendAsync(new ListPins { Hash = chunk.Hash }, None)).Value.Pins);
        Assert.Equal(Status.NotFound, (await client.SendAsync(new PinObject { Hash = Samples.Chunk(2).Hash }, None)).Status);
        Assert.Equal((ushort)1, (await client.SendAsync(new Unpin { Hashes = [chunk.Hash] }, None)).Value);
        Assert.Equal((ushort)0, (await client.SendAsync(new Unpin { Hashes = [chunk.Hash] }, None)).Value);
    }

    // §5.11, §9.6: a service's pin has reason Service and source H(name); the user's Unpin leaves it; ReleasePins with
    // that source removes it; ReleasePins of a Host reason is Invalid.
    [Fact]
    public async Task AServicePinIsReasonServiceSourceHOfItsName_ReleasedOnlyByReleasePins()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient user = await node.ConnectUserAsync("app", None);
        LocalClient cli = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        Chunk chunk = Samples.Chunk(3);
        Assert.True(await node.Node.PutAsync(chunk, None));
        Hash32 source = Hash32.Of(Encoding.UTF8.GetBytes("cli"));

        Pin pin = (await cli.SendAsync(new PinObject { Hash = chunk.Hash }, None)).Value;

        Assert.Equal((LocalPinReason.Service, source), (pin.Reason, pin.Source));
        Assert.Equal((ushort)0, (await user.SendAsync(new Unpin { Hashes = [chunk.Hash] }, None)).Value);
        Assert.Equal(Status.Invalid, (await user.SendAsync(new ReleasePins { Reason = LocalPinReason.Head, Source = source }, None)).Status);
        Assert.Equal(Status.NotPermitted, (await cli.SendAsync(new ReleasePins { Reason = LocalPinReason.Service, Source = source }, None)).Status);
        Assert.Equal(1u, (await user.SendAsync(new ReleasePins { Reason = LocalPinReason.Service, Source = source }, None)).Value);
        Assert.Empty((await user.SendAsync(new ListPins { Hash = chunk.Hash }, None)).Value.Pins);
    }

    // §5.11, §6.5: pins come ascending by (hash, reason, source); a page after a cursor holds the rest; hashes to Unpin
    // that are not ascending are Invalid.
    [Fact]
    public async Task PinsListAscendingByHashReasonSource_AfterTheCursor()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient user = await node.ConnectUserAsync("app", None);
        LocalClient cli = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        Chunk[] chunks = [Samples.Chunk(4), Samples.Chunk(5), Samples.Chunk(6)];
        foreach (Chunk chunk in chunks)
        {
            Assert.True(await node.Node.PutAsync(chunk, None));
            Assert.True((await user.SendAsync(new PinObject { Hash = chunk.Hash }, None)).IsOk);
            Assert.True((await cli.SendAsync(new PinObject { Hash = chunk.Hash }, None)).IsOk);
        }

        Pin[] all = [.. (await user.SendAsync(new ListPins(), None)).Value.Pins.Where(p => chunks.Any(c => c.Hash == p.Hash))];
        PinKey cursor = new() { Hash = all[2].Hash, Reason = all[2].Reason, Source = all[2].Source };
        Pin[] rest = [.. (await user.SendAsync(new ListPins { After = cursor }, None)).Value.Pins.Where(p => chunks.Any(c => c.Hash == p.Hash))];

        var expected = chunks.Select(c => c.Hash).Order().SelectMany(h => new[] { (h, LocalPinReason.User), (h, LocalPinReason.Service) }).ToArray();
        Assert.Equal(expected, all.Select(p => (p.Hash, p.Reason)));
        Assert.Equal(all[3..], rest);
        ObjectHash[] descending = [.. chunks.Select(c => c.Hash).OrderDescending()];
        Assert.Equal(Status.Invalid, (await user.SendAsync(new Unpin { Hashes = descending }, None)).Status);
    }

    // §6.5: SetStoreBudget sets the budget, 0 the device default; a local write that would put the pinned bytes over the
    // budget is StoreFull and stores nothing.
    [Fact]
    public async Task ABudgetBelowThePinnedBytesMakesALocalWriteStoreFull_Budget0IsTheDefault()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        await node.HoldUserKeyAsync(None);
        LocalClient client = await node.ConnectUserAsync("app", None);
        ulong pinned = (await client.SendAsync(new GetStore(), None)).Value.PinnedBytes;

        Store tight = (await client.SendAsync(new SetStoreBudget { Bytes = pinned + 10 }, None)).Value;
        Status full = (await client.SendAsync(new CreatePackage { Kind = PackageKind.Channel, Name = "Family" }, None)).Status;
        Store relaxed = (await client.SendAsync(new SetStoreBudget { Bytes = 0 }, None)).Value;

        Assert.Equal(pinned + 10, tight.Budget);
        Assert.Equal(Status.StoreFull, full);
        Assert.Empty((await client.SendAsync(new ListPackages(), None)).Value.Packages);
        Assert.Equal(0ul, relaxed.Budget);
        Assert.True((await client.SendAsync(new CreatePackage { Kind = PackageKind.Channel, Name = "Family" }, None)).IsOk);
    }
}
