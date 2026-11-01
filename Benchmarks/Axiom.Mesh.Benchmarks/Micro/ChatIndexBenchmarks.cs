using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;
using BenchmarkDotNet.Attributes;

namespace Axiom.Mesh.Benchmarks.Micro;

/// <summary>
/// The chat service's index on disk (plan §9.3 item 6): a timeline page of 256 out of a Channel of 100 000 messages (target
/// 5 ms, floor 50 ms), one message taken in, one redaction applied, each its own transaction at full durability.
/// </summary>
[MemoryDiagnoser]
public class ChatIndexBenchmarks
{
    private const int Messages = 100_000;
    private const int Batch = 1_000;
    private static readonly PackageId Channel = new(Hash32.Of("bench channel"u8));
    private static readonly DeviceId Device = new(Hash32.Of("bench device"u8));
    private static readonly ChannelOwner Owner = new(new UserId(Hash32.Of("bench owner"u8)), []);
    private string directory = string.Empty;
    private Database database = null!;
    private ChatIndex index = null!;
    private EntryKey middle;
    private ulong next = Messages;

    /// <summary>Opens a database on disk and fills one Channel with 100 000 messages, a thousand per transaction.</summary>
    /// <returns>A task.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        directory = Path.Combine(Path.GetTempPath(), "axiom-mesh-bench", "chat-" + Guid.NewGuid().ToString("N")[..8]);
        database = await ChatDatabase.OpenDirectoryAsync(directory, CancellationToken.None).ConfigureAwait(false);
        index = await ChatDatabase.OpenAsync(database, CancellationToken.None).ConfigureAwait(false);
        await index.UpsertChannelAsync(new Package { PackageId = Channel, State = PackageState.Followed, Kind = PackageKind.Channel, Name = "bench", OwnerId = Owner.Owner }, CancellationToken.None).ConfigureAwait(false);
        for (int start = 0; start < Messages; start += Batch)
        {
            await using LdbContext context = await LdbContext.OpenAsync(database, CancellationToken.None).ConfigureAwait(false);
            for (int i = start; i < start + Batch; i++)
                context.Set<MessageRow>().Add(Row((ulong)i, $"message {i}"));
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        middle = ChatIndex.KeyOf(Row(Messages / 2, string.Empty));
    }

    /// <summary>Closes and deletes the database.</summary>
    /// <returns>A task.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await database.DisposeAsync().ConfigureAwait(false);
        Directory.Delete(directory, recursive: true);
    }

    /// <summary>The page of 256 after the middle of 100 000: the index read, the views and the cut to 1 MiB.</summary>
    /// <returns>The page.</returns>
    [Benchmark]
    public async Task<TimelineReply> TimelinePage256Of100k()
    {
        List<MessageRow> rows = await index.PageAsync(Channel, middle, false, ChatLimits.TimelinePage + 1, CancellationToken.None).ConfigureAwait(false);
        return TimelinePage.Cut([.. rows.Select(r => RowViews.Message(r, new AuthorView { DeviceId = Device }))], middle, false, ChatLimits.TimelinePage, ChatLimits.PayloadBytes);
    }

    /// <summary>One message taken in from the stream: dedupe, rows, counts and the meta row in one transaction.</summary>
    /// <returns>Whether it did what it measures.</returns>
    [Benchmark]
    public async Task<bool> IndexOneMessage()
    {
        ulong clock = Interlocked.Increment(ref next);
        return (await index.IndexAsync(Message(clock), Owner, advance: true, CancellationToken.None).ConfigureAwait(false)).Change == IndexChange.NewMessage;
    }

    /// <summary>One redaction applied to a message indexed before, by the message's own device.</summary>
    /// <returns>Whether it did what it measures.</returns>
    [Benchmark]
    public async Task<bool> ApplyOneRedaction()
    {
        ulong clock = Interlocked.Increment(ref next);
        Entry target = Message(clock);
        await index.IndexAsync(target, Owner, advance: false, CancellationToken.None).ConfigureAwait(false);
        EntryBody body = new RedactionPayload { Target = target.Hash }.ToBody();
        var redaction = new Entry { Hash = new ObjectHash(Hash32.Of(BitConverter.GetBytes(clock + (1UL << 50)))), StoreSeq = clock, PackageId = Channel, DeviceId = Device, Clock = clock + 1, Kind = EntryKind.Redaction, Redacts = body.Redacts, Payload = body.Payload };
        return (await index.IndexAsync(redaction, Owner, advance: true, CancellationToken.None).ConfigureAwait(false)).Change == IndexChange.Withdrew;
    }

    private static MessageRow Row(ulong clock, string text) => new()
    {
        Hash = RowBytes.Of(Hash32.Of(BitConverter.GetBytes(clock))),
        Package = RowBytes.Of(Channel.Value),
        Clock = clock,
        Device = RowBytes.Of(Device.Value),
        StoredMs = 1_800_000_000_000,
        Text = text,
        Attachments = AttachmentsBlob.Of([]),
    };

    private static Entry Message(ulong clock)
    {
        EntryBody body = new MessagePayload { Text = $"message {clock}", Attachments = [], ReplyTo = default }.ToBody();
        return new Entry { Hash = new ObjectHash(Hash32.Of(BitConverter.GetBytes(clock))), StoreSeq = clock, PackageId = Channel, DeviceId = Device, Clock = clock, Kind = EntryKind.Message, Payload = body.Payload };
    }
}
