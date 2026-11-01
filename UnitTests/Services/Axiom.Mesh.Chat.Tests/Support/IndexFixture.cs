using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Chat.Tests.Support;

/// <summary>A chat index on LDB's in-memory storage, with Entries made the way the Host hands them over (LOCAL-SPEC.md §5.6).</summary>
internal sealed class IndexFixture : IAsyncDisposable
{
    private static readonly CancellationToken None = CancellationToken.None;

    private IndexFixture(Database database, ChatIndex index)
    {
        Database = database;
        Index = index;
    }

    public Database Database { get; }

    public ChatIndex Index { get; }

    /// <summary>The owner Alice, her laptop and phone, and Bob's desktop.</summary>
    public static ChannelOwner Owner { get; } = new(Facts.Alice, Facts.Devices);

    public static async Task<IndexFixture> StartAsync()
    {
        Database database = await Database.OpenAsync(new InMemoryStorageEnvironment(), DatabaseOptions.Default, None);
        ChatIndex index = await ChatDatabase.OpenAsync(database, None);
        await index.SetEpochAsync(Facts.Id(0xe1), None);
        await index.UpsertChannelAsync(new Package { PackageId = Facts.Family, State = PackageState.Followed, OwnerId = Facts.Alice, Kind = PackageKind.Channel, Name = "Family" }, None);
        return new IndexFixture(database, index);
    }

    /// <summary>A Message Entry as stored.</summary>
    public static Entry Message(byte hash, ulong clock, DeviceId device, string text, ulong storeSeq, ObjectHash replyTo = default, IReadOnlyList<Attachment>? attachments = null)
    {
        EntryBody body = new MessagePayload { Text = text, Attachments = attachments ?? [], ReplyTo = replyTo }.ToBody();
        return new Entry
        {
            Hash = Facts.Hash(hash),
            StoreSeq = storeSeq,
            Stored = 1_800_000_000_000 + storeSeq,
            PackageId = Facts.Family,
            DeviceId = device,
            Clock = clock,
            Kind = EntryKind.Message,
            References = [.. body.References],
            Payload = body.Payload,
        };
    }

    /// <summary>A Redaction Entry as stored.</summary>
    public static Entry Redaction(byte hash, ulong clock, DeviceId device, ObjectHash target, ulong storeSeq)
    {
        EntryBody body = new RedactionPayload { Target = target }.ToBody();
        return new Entry
        {
            Hash = Facts.Hash(hash),
            StoreSeq = storeSeq,
            Stored = 1_800_000_000_000 + storeSeq,
            PackageId = Facts.Family,
            DeviceId = device,
            Clock = clock,
            Kind = EntryKind.Redaction,
            Redacts = body.Redacts,
            Payload = body.Payload,
        };
    }

    public Task<IndexResult> TakeInAsync(Entry entry, bool advance = true) => Index.IndexAsync(entry, Owner, advance, None);

    public async Task<IReadOnlyList<EntryKey>> KeysAsync(bool reverse, int pageSize)
    {
        var keys = new List<EntryKey>();
        EntryKey? after = null;
        while (true)
        {
            List<MessageRow> page = await Index.PageAsync(Facts.Family, after, reverse, pageSize, None);
            keys.AddRange(page.Select(ChatIndex.KeyOf));
            if (page.Count < pageSize)
                return keys;
            after = keys[^1];
        }
    }

    public async ValueTask DisposeAsync() => await Database.DisposeAsync();
}
