using System;
using System.Linq;

namespace Axiom.Mesh.Sync.Client.Tests.Rules;

/// <summary>
/// The bounds of the namespace's records (SYNC-SERVICE.md §4, §9): 64 bindings, 64 ignore patterns, a 4 096-byte path and a page of
/// 1 024 conflicts are written; one more of any is refused by the writer, and a payload that carries it does not read.
/// </summary>
public sealed class RecordBoundsTests
{
    [Fact]
    public void EveryBoundIsWrittenAtItsValue_AndRefusedOneOver()
    {
        var atBindings = new BindingsReply { Bindings = [.. Enumerable.Range(0, SyncLimits.Bindings).Select(_ => new Binding())] };
        var atIgnore = new BindRequest { Directory = "/d", Ignore = [.. Enumerable.Range(0, SyncLimits.IgnorePatterns).Select(i => $"*.x{i}")] };
        var atPath = new StatusRequest { Directory = new string('p', SyncLimits.PathLength) };
        var atPage = new ConflictsReply { Conflicts = [.. Enumerable.Range(0, SyncLimits.ConflictsPage).Select(i => new Conflict { Id = (ulong)i })] };

        Assert.True(SyncCodec.TryDecode(SyncCodec.Encode(atBindings), out BindingsReply? bindings) && bindings.Bindings.Count == SyncLimits.Bindings);
        Assert.True(SyncCodec.TryDecode(SyncCodec.Encode(atIgnore), out BindRequest? ignore) && ignore.Ignore.Count == SyncLimits.IgnorePatterns);
        Assert.True(SyncCodec.TryDecode(SyncCodec.Encode(atPath), out StatusRequest? _));
        Assert.True(SyncCodec.TryDecode(SyncCodec.Encode(atPage), out ConflictsReply? page) && page.Conflicts.Count == SyncLimits.ConflictsPage);
        Assert.Throws<ArgumentException>(() => SyncCodec.Encode(atBindings with { Bindings = [.. atBindings.Bindings, new Binding()] }));
        Assert.Throws<ArgumentException>(() => SyncCodec.Encode(atIgnore with { Ignore = [.. atIgnore.Ignore, "one more"] }));
        Assert.Throws<ArgumentException>(() => SyncCodec.Encode(atPath with { Directory = atPath.Directory + "p" }));
        Assert.Throws<ArgumentException>(() => SyncCodec.Encode(atPage with { Conflicts = [.. atPage.Conflicts, new Conflict()] }));
        Assert.Throws<ArgumentException>(() => SyncCodec.Encode(new ConflictsRequest { Limit = 0 }));
    }

    [Fact]
    public void APayloadWithTrailingBytesOrALimitOutOfBoundsDoesNotRead()
    {
        byte[] reply = SyncCodec.Encode(new ScanReply { Queued = 1 });
        byte[] noLimit = [0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0x00, 0x00];
        byte[] trailing = [.. reply, 0];

        Assert.False(SyncCodec.TryDecode(trailing, out ScanReply? _));
        Assert.False(SyncCodec.TryDecode(noLimit, out ConflictsRequest? _));
    }
}
