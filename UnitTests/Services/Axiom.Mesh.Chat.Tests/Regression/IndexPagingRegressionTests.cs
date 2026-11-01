using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Chat.Tests.Support;

namespace Axiom.Mesh.Chat.Tests.Regression;

/// <summary>
/// Paging the timeline through LDB's one-column clock index (CHAT-SERVICE.md §10): ties of one clock are put in order in
/// memory, so a page boundary that falls inside one clock neither skips nor repeats a message, forward or backward.
/// </summary>
public sealed class IndexPagingRegressionTests
{
    // Damage: rows of one clock come out of the index in row order; a page cut inside a clock and continued "after" its last
    // key without reading that clock whole would skip the clock's other messages or show some twice.
    [Fact]
    public async Task PagesCutInsideOneClockNeitherSkipNorRepeat_ForwardOrBackward()
    {
        await using IndexFixture fixture = await IndexFixture.StartAsync();
        var expected = new List<EntryKey>();
        DeviceId[] devices = [Facts.Laptop, Facts.Desktop, Facts.Phone];
        byte hash = 1;
        for (ulong clock = 0; clock < 4; clock++)
        {
            int ties = clock == 2 ? 9 : 2;
            for (int i = 0; i < ties; i++, hash++)
            {
                Mesh.Local.Entry entry = IndexFixture.Message(hash, clock, devices[i % devices.Length], $"m{hash}", hash);
                await fixture.TakeInAsync(entry);
                expected.Add(entry.Key);
            }
        }

        expected.Sort();
        foreach (int size in new[] { 1, 2, 3, 4, 5, 7 })
        {
            Assert.Equal(expected, await fixture.KeysAsync(reverse: false, size));
            Assert.Equal(expected.AsEnumerable().Reverse(), await fixture.KeysAsync(reverse: true, size));
        }
    }
}
