using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Service.Tests.Support;

namespace Axiom.Mesh.Service.Tests.Rules;

/// <summary>
/// A service's state (S-D2, LOCAL-SPEC.md §9.3): one database for the service's own rows and the runtime's cursor, which
/// records the store epoch and the last store sequence handled, survives a reopening of the same storage, and starts over
/// under a new epoch.
/// </summary>
public sealed class StateTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // S-D2, plan §9.2 item 4 ("check first"): the service's own entity, from its own assembly, and the runtime's cursor
    // live in one database and commit independently.
    [Fact]
    public async Task TheServicesOwnRowsAndTheRuntimesCursorShareOneDatabase()
    {
        await using Database database = await Database.OpenAsync(new InMemoryStorageEnvironment(), DatabaseOptions.Default, None);
        ServiceState state = await ServiceState.OpenAsync(database, None);
        await using (LdbContext context = await LdbContext.OpenAsync(state.Database, None))
        {
            await context.EnsureTableAsync<NoteRow>(None);
            context.Set<NoteRow>().Add(new NoteRow { Key = "a", Text = "first" });
            await context.SaveChangesAsync(None);
        }

        await state.AdvanceAsync(Hash32.Of([1]), 7, None);

        await using LdbContext reading = await LdbContext.OpenAsync(database, None);
        Assert.Equal("first", (await reading.Set<NoteRow>().FindAsync("a", None))!.Text);
        Assert.Equal(new IndexCursor { Epoch = Hash32.Of([1]), LastSeq = 7 }, await state.ReadCursorAsync(None));
    }

    // §9.3: the cursor is none before the first Entry, survives a reopening of the same storage, and a discard under another
    // epoch starts it at 0 of that epoch.
    [Fact]
    public async Task TheCursorSurvivesAReopening_AndADiscardStartsItOverUnderTheNewEpoch()
    {
        var storage = new InMemoryStorageEnvironment();
        await using (Database first = await Database.OpenAsync(storage, DatabaseOptions.Default, None))
        {
            ServiceState state = await ServiceState.OpenAsync(first, None);
            Assert.Equal(IndexCursor.None, await state.ReadCursorAsync(None));
            await state.AdvanceAsync(Hash32.Of([1]), 41, None);
            await state.AdvanceAsync(Hash32.Of([1]), 42, None);
        }

        await using Database second = await Database.OpenAsync(storage, DatabaseOptions.Default, None);
        ServiceState reopened = await ServiceState.OpenAsync(second, None);
        Assert.Equal(new IndexCursor { Epoch = Hash32.Of([1]), LastSeq = 42 }, await reopened.ReadCursorAsync(None));

        await reopened.DiscardAsync(Hash32.Of([2]), None);
        Assert.Equal(new IndexCursor { Epoch = Hash32.Of([2]), LastSeq = 0 }, await reopened.ReadCursorAsync(None));
    }
}
