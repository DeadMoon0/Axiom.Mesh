using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §6.4, §8.6, the operation kinds: <c>Fetch</c> is refused at once for an object no stored Entry of the
/// Package references, waits for a source and fails <c>NotFound</c> after 60 seconds without one, completes as soon as a
/// source appears (no clock has to move), and a cancelled Fetch cancels its wants. <c>Materialise</c> and <c>PutFile</c>
/// left the Host in draft 4; the client's helpers are proven by <c>StoreFilesTests</c>.
/// </summary>
public sealed class OperationKindTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §6.4: packageId is a followed Package whose Entries reference the object; an object none of its stored Entries
    // reaches through the reference rows is NotFound before anything is wanted.
    [Fact]
    public async Task AFetchOfAnObjectNoStoredEntryOfThePackageReferencesIsNotFoundAtOnce()
    {
        await using OnDemandArchive fixture = await OnDemandArchive.StartAsync();
        LocalClient client = await fixture.Bob.ConnectUserAsync("app", None);
        ObjectHash stranger = ObjectFixtures.Folder(("c.txt", ObjectFixtures.Content(100, 9))).Root.Hash;

        LocalResult<uint> refused = await client.SendAsync(new Fetch { PackageId = fixture.Archive, Hash = stranger }, None);

        Assert.Equal(Status.NotFound, refused.Status);
        Assert.False(fixture.Bob.Node.Replicating.Wants.IsWanted(stranger));
    }

    // §6.4, §8.6: with no established peer that could supply it, a Fetch waits; at 60 seconds it fails NotFound, its
    // message naming the hash.
    [Fact]
    public async Task AFetchWithoutASourceWaits_AndFailsNotFoundAfter60Seconds()
    {
        await using OnDemandArchive fixture = await OnDemandArchive.StartAsync();
        LocalClient client = await fixture.Bob.ConnectUserAsync("app", None);
        ObjectHash older = fixture.Older.Root.Hash;

        uint id = (await client.SendAsync(new Fetch { PackageId = fixture.Archive, Hash = older }, None)).Value;
        Operation waiting = await UntilAsync(fixture.Bob, id, o => o.Message.StartsWith("waiting for a source", StringComparison.Ordinal), "the Fetch to wait");
        fixture.Mesh.Clock.Advance(TimeSpan.FromSeconds(Limits.FetchWithoutSourceSeconds - 1));
        Assert.Equal(OperationState.Running, (await client.SendAsync(new GetOperation { OperationId = id }, None)).Value.State);
        fixture.Mesh.Clock.Advance(TimeSpan.FromSeconds(1));
        Operation failed = await UntilAsync(fixture.Bob, id, o => o.State != OperationState.Running, "the Fetch to end");

        Assert.Equal(OperationState.Running, waiting.State);
        Assert.Equal((OperationState.Failed, Status.NotFound), (failed.State, failed.Status));
        Assert.Contains(older.ToString(), failed.Message, StringComparison.Ordinal);
    }

    // §6.4, §8.6, SPEC.md §10.5: once a peer that lists the Package is established, the Fetch wants from it at once, with
    // no clock moving, and ends Done with the objects and bytes fetched; recursive fetches everything below a Tree; pin
    // pins with reason User.
    [Fact]
    public async Task AFetchCompletesAsSoonAsASourceAppears_RecursivelyAndPinned()
    {
        await using OnDemandArchive fixture = await OnDemandArchive.StartAsync();
        LocalClient client = await fixture.Bob.ConnectUserAsync("app", None);
        TreeFixture folder = fixture.Older;
        uint id = (await client.SendAsync(new Fetch { PackageId = fixture.Archive, Hash = folder.Root.Hash, Recursive = true, Pin = true }, None)).Value;
        await UntilAsync(fixture.Bob, id, o => o.Message.StartsWith("waiting for a source", StringComparison.Ordinal), "the Fetch to wait");

        await fixture.Mesh.StartNodeAsync("alice-laptop", None);
        await fixture.Mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        Operation done = await UntilAsync(fixture.Bob, id, o => o.State != OperationState.Running, "the Fetch to end");

        Assert.Equal(OperationState.Done, done.State);
        FetchResult result = ResultOf<FetchResult>(done);
        int objects = folder.Objects.Select(o => o.Hash).Distinct().Count();
        Assert.Equal((uint)objects, result.Objects);
        Assert.Equal((ulong)folder.Objects.DistinctBy(o => o.Hash).Sum(o => o.Bytes.Length), result.Bytes);
        foreach (StorableObject stored in folder.Objects)
            Assert.True(await fixture.Bob.Node.ContainsAsync(stored.Hash, None));
        Assert.Contains((await client.SendAsync(new ListPins { Hash = folder.Root.Hash }, None)).Value.Pins, p => p.Reason == Axiom.Mesh.Local.PinReason.User);
    }

    // §8.5: CancelOperation on a waiting Fetch ends it Cancelled, and the node no longer wants what it fetched.
    [Fact]
    public async Task ACancelledFetchEndsCancelled_AndItsWantIsCancelled()
    {
        await using OnDemandArchive fixture = await OnDemandArchive.StartAsync();
        LocalClient client = await fixture.Bob.ConnectUserAsync("app", None);
        ObjectHash root = fixture.Older.Root.Hash;
        uint id = (await client.SendAsync(new Fetch { PackageId = fixture.Archive, Hash = root }, None)).Value;
        await UntilAsync(fixture.Bob, id, o => o.Message.StartsWith("waiting for a source", StringComparison.Ordinal), "the Fetch to wait");
        Assert.True(fixture.Bob.Node.Replicating.Wants.IsWanted(root));

        Operation cancelled = (await client.SendAsync(new CancelOperation { OperationId = id }, None)).Value;

        Assert.Equal((OperationState.Cancelled, Status.Cancelled), (cancelled.State, cancelled.Status));
        await Waits.UntilAsync(() => !fixture.Bob.Node.Replicating.Wants.IsWanted(root), "the want to be cancelled");
    }

    // §8.6: an operation's result body is the kind's record.
    private static T ResultOf<T>(Operation operation)
        where T : class, IWireRecord<T>
    {
        var reader = new Axiom.ProtoStream.Codecs.WireReader(operation.Result);
        Assert.True(T.TryRead(ref reader, out T? value));
        return value!;
    }

    private static async Task<Operation> UntilAsync(TestNode node, uint id, Func<Operation, bool> match, string what, string client = "app")
    {
        Operation? seen = null;
        await Waits.UntilAsync(
            () => node.Node.Local.Book.Live().FirstOrDefault(c => c.Hello?.Name == client)?.Operations.Get(id) is { } now && match(seen = now),
            what);
        return seen!;
    }
}
