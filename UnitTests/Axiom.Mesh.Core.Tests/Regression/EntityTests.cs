using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.LDB.Schema;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>The index's tables as LDB stores them: every entity round-trips, and every table keeps its schema.</summary>
public sealed class EntityTests
{
    /// <summary>The schema of every table, as the generator makes it; a change here is a migration.</summary>
    private static readonly Dictionary<string, string> Schemas = new()
    {
        ["Users"] = "User Bytes(32) | UserKey Bytes(65) | Paired Bool | Roster Bytes(32)? | RosterSeq UInt64 | SinceMs Int64 | Name String(64) || ByUser(User) unique",
        ["Devices"] = "Key Bytes(64) | User Bytes(32) | Device Bytes(32) | DeviceKey Bytes(65) | Name String(64) | Listed Bool | Removal Int32 | RemovedEver Bool || ByKey(Key) unique | ByUser(User) | ByDevice(Device)",
        ["Packages"] = "Package Bytes(32) | State UInt8 | Manifest Bytes(32)? | ManifestSeq UInt64 | Policy UInt8 | FollowedSinceMs Int64 | NewestVersion Bytes(32)? | NewestVersionClock UInt64 | NewestVersionDevice Bytes(32)? || ByPackage(Package) unique",
        ["Grants"] = "Key Bytes(64) | Package Bytes(32) | Grantee Bytes(32) | Grant Bytes(32) | Rights UInt8 | Seq UInt64 || ByKey(Key) unique | ByPackage(Package)",
        ["Heads"] = "Key Bytes(64) | Package Bytes(32) | Device Bytes(32) | Entry Bytes(32) | Clock UInt64 || ByKey(Key) unique | ByPackage(Package)",
        ["Entries"] = "Hash Bytes(32) | StoreSeq UInt64 | Package Bytes(32) | Device Bytes(32) | Clock UInt64 | Parents Bytes(64) | Kind UInt16 | Target Bytes(32)? | Redacted Bool | StoredMs Int64 || ByHash(Hash) unique | ByStoreSeq(StoreSeq) unique | ByPackage(Package) | ByClock(Clock) | ByTarget(Target)",
        ["Objects"] = "Hash Bytes(32) | Tag UInt8 | Size Int64 | StoredMs Int64 | LastUsedMs Int64 | PinCount Int32 | EvictPending Bool || ByHash(Hash) unique | ByLastUsedMs(LastUsedMs)",
        ["Pins"] = "Key Bytes(65) | Hash Bytes(32) | Reason UInt8 | Source Bytes(32) | SinceMs Int64 || ByKey(Key) unique | ByHash(Hash) | BySource(Source)",
        ["Peers"] = "Device Bytes(32) | User Bytes(32)? | Addresses String | FirstSeenMs Int64 | LastSeenMs Int64 | BackoffUntilMs Int64 | Violations Int32 | ViolationsSinceMs Int64 || ByDevice(Device) unique",
        ["Meta"] = "Name String(32) | Bytes Bytes(65)? | Number UInt64 || ByName(Name) unique",
        ["References"] = "Key Bytes(64) | Referrer Bytes(32) | Target Bytes(32) || ByKey(Key) unique | ByReferrer(Referrer) | ByTarget(Target)",
    };

    // Damage: a renamed, retyped, resized or re-indexed column changes the file format of every existing store; the
    // snapshot makes that a deliberate decision with a migration, never a side effect of a refactoring.
    [Fact]
    public void EveryTableKeepsItsSchema()
    {
        TableSchema[] tables =
        [
            UserRow.Model.Schema, DeviceRow.Model.Schema, PackageRow.Model.Schema, GrantRow.Model.Schema, HeadRow.Model.Schema,
            EntryRow.Model.Schema, ObjectRow.Model.Schema, PinRow.Model.Schema, PeerRow.Model.Schema, MetaRow.Model.Schema,
            ReferenceRow.Model.Schema,
        ];

        Assert.Equal(Schemas.Keys.Order(), tables.Select(t => t.Name).Order());
        Assert.All(tables, t => Assert.Equal(Schemas[t.Name], Describe(t)));
    }

    // Damage: a column the generator stops writing or reads back converted (a byte array cut to its inline length, an
    // enum widened, a nullable read as zero) loses index state silently; every entity, with every column set, comes
    // back equal.
    [Fact]
    public async Task EveryEntityRoundTripsThroughItsTable()
    {
        await using TestStore node = await TestStore.StartAsync();
        byte[] a = Bytes(1, 32);
        byte[] b = Bytes(2, 32);
        await RoundTripAsync(node, new UserRow { User = a, UserKey = Bytes(3, 65), Paired = true, Roster = b, RosterSeq = 9, SinceMs = -5, Name = "Bob" });
        await RoundTripAsync(node, new DeviceRow { Key = Bytes(4, 64), User = a, Device = b, DeviceKey = Bytes(5, 65), Name = "laptöp", Listed = true, Removal = DeviceRemoval.HeldByAnotherUser, RemovedEver = true });
        await RoundTripAsync(node, new PackageRow { Package = a, State = PackageState.Followed, Manifest = b, ManifestSeq = ulong.MaxValue, Policy = LocalRetention.LatestOnly, FollowedSinceMs = 7, NewestVersion = a, NewestVersionClock = 8, NewestVersionDevice = b });
        await RoundTripAsync(node, new GrantRow { Key = Bytes(6, 64), Package = a, Grantee = b, Grant = a, Rights = GrantRights.Write, Seq = 3 });
        await RoundTripAsync(node, new HeadRow { Key = Bytes(7, 64), Package = a, Device = b, Entry = a, Clock = (1UL << 63) - 1 });
        await RoundTripAsync(node, new EntryRow { Hash = a, StoreSeq = 4, Package = b, Device = a, Clock = 5, Parents = Bytes(8, 64), Kind = (EntryKind)999, Target = b, Redacted = true, StoredMs = 6 });
        await RoundTripAsync(node, new ObjectRow { Hash = a, Tag = 200, Size = Limits.MaxObjectLength, StoredMs = 1, LastUsedMs = 2, PinCount = 3, EvictPending = true });
        await RoundTripAsync(node, new PinRow { Key = Bytes(9, 65), Hash = a, Reason = PinReason.Outbound, Source = b, SinceMs = 10 });
        await RoundTripAsync(node, new PeerRow { Device = a, User = b, Addresses = "192.0.2.1:41810\n[2001:db8::1]:41810", FirstSeenMs = 1, LastSeenMs = 2, BackoffUntilMs = 3, Violations = 4, ViolationsSinceMs = 5 });
        await RoundTripAsync(node, new MetaRow { Name = "test.value", Bytes = Bytes(10, 65), Number = 11 });
        await RoundTripAsync(node, new ReferenceRow { Key = Bytes(11, 64), Referrer = a, Target = b });
    }

    private static async Task RoundTripAsync<T>(TestStore node, T row)
        where T : class, IEntity<T>
    {
        await using (LdbContext context = await LdbContext.OpenAsync(node.Database, CancellationToken.None))
        {
            context.Set<T>().Add(row);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        object?[] expected = T.Model.Values(row);
        T read = Assert.Single(await node.RowsAsync<T>(), r => Same(T.Model.Values(r)[T.Model.Key], expected[T.Model.Key]));
        object?[] actual = T.Model.Values(read);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(Same(expected[i], actual[i]), $"{typeof(T).Name}.{T.Model.Schema.Columns[i].Name}: wrote {expected[i]}, read {actual[i]}.");
    }

    private static bool Same(object? x, object? y) =>
        x is byte[] left && y is byte[] right ? left.AsSpan().SequenceEqual(right) : Equals(x, y);

    private static byte[] Bytes(byte fill, int length) => [.. Enumerable.Range(0, length).Select(i => (byte)(fill + i))];

    private static string Describe(TableSchema table) =>
        string.Join(" | ", table.Columns.Select(c => $"{c.Name} {c.Type}{(c.MaxLength > 0 ? $"({c.MaxLength})" : string.Empty)}{(c.Nullable ? "?" : string.Empty)}"))
        + " || "
        + string.Join(" | ", table.Indexes.Select(i => $"{i.Name}({string.Join(",", i.Columns)}){(i.Unique ? " unique" : string.Empty)}{(i.Hashed ? " hashed" : string.Empty)}"));
}
