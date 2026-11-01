using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Chat;

using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Integration.Tests.Support;

/// <summary>
/// The chat service's release floors measured on two real nodes over loopback TLS and the real pipe, the service in process
/// beside each node (plan §9.3 item 6): a 1 MiB attachment from Alice's <c>chat send</c> to the file Bob's <c>chat get</c>
/// wrote, and Bob's service catching up with messages stored while it was stopped. The floor tests and the benchmark
/// scenarios <c>chat-send</c> and <c>chat-catchup</c> run the same code.
/// </summary>
internal static class ChatMeasure
{
    private const string Alice = "alice-laptop";
    private const string Bob = "bob-desktop";
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>Alice sends a 1 MiB file through her chat service; Bob fetches it with chat get: the time from send to written.</summary>
    /// <returns>How long it took.</returns>
    public static async Task<TimeSpan> MeasureSendAsync()
    {
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync([Alice, Bob], [], [], [ChatProgram.Definition], None).ConfigureAwait(false);
        PackageId channel = await SharedChannelAsync(mesh).ConfigureAwait(false);
        await using RunningService aliceChat = await mesh.RunServiceAsync(Alice, new ChatService(), None).ConfigureAwait(false);
        await using RunningService bobChat = await mesh.RunServiceAsync(Bob, new ChatService(), None).ConfigureAwait(false);
        string folder = Path.Combine(Path.GetTempPath(), "axiom-mesh-bench", "chat-send-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "photo.bin");
        await File.WriteAllBytesAsync(file, [.. Enumerable.Range(0, 1 << 20).Select(i => (byte)(i * 31 + (i >> 12)))]).ConfigureAwait(false);
        try
        {
            var watch = Stopwatch.StartNew();
            CliResult sent = await mesh.CliAsync(Alice, $"chat send {channel.Value.ToHex()} --text photo --file '{file}'", None).ConfigureAwait(false);
            string entry = System.Text.RegularExpressions.Regex.Match(sent.Stdout, "entry ([0-9a-f]{64})").Groups[1].Value;
            await UntilAsync(mesh, Bob, async app => (await ChatCall.SendAsync(app, new MessageRequest { Hash = new ObjectHash(Hash32.TryParseHex(entry, out Hash32 h) ? h : default) }, None).ConfigureAwait(false)).IsOk).ConfigureAwait(false);
            CliResult got = await mesh.CliAsync(Bob, $"chat get {entry} photo.bin --out '{Path.Combine(folder, "copy.bin")}'", None).ConfigureAwait(false);
            watch.Stop();
            if (got.ExitCode != Cli.CliExitCode.Ok || !File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "copy.bin"))))
                throw new InvalidOperationException("The attachment did not arrive byte-equal: " + got);
            return watch.Elapsed;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Alice writes the messages while Bob's service is stopped; then Bob's service starts: the time until it caught up.</summary>
    /// <param name="messages">How many messages.</param>
    /// <returns>How long the catch-up took.</returns>
    public static async Task<TimeSpan> MeasureCatchUpAsync(int messages)
    {
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync([Alice, Bob], [], [], [ChatProgram.Definition], None).ConfigureAwait(false);
        PackageId channel = await SharedChannelAsync(mesh).ConfigureAwait(false);
        await using RunningService aliceChat = await mesh.RunServiceAsync(Alice, new ChatService(), None).ConfigureAwait(false);
        await using (LocalClient app = await mesh.ConnectUserAsync(Alice, "bench", None).ConfigureAwait(false))
        {
            for (int i = 0; i < messages; i++)
            {
                LocalResult<SendReply> sent = await ChatCall.SendAsync(app, new SendRequest { PackageId = channel, Text = $"message {i}" }, None).ConfigureAwait(false);
                if (!sent.IsOk)
                    throw new InvalidOperationException("send: " + sent);
            }
        }

        await SettleLongAsync(mesh).ConfigureAwait(false);
        var watch = Stopwatch.StartNew();
        await using RunningService bobChat = await mesh.RunServiceAsync(Bob, new ChatService(), None).ConfigureAwait(false);
        await UntilAsync(mesh, Bob, async app =>
        {
            LocalResult<ChannelsReply> channels = await ChatCall.SendAsync(app, new ChannelsRequest(), None).ConfigureAwait(false);
            return channels.IsOk && channels.Value.Channels.Any(c => c.Messages >= (ulong)messages);
        }).ConfigureAwait(false);
        watch.Stop();
        return watch.Elapsed;
    }

    // Settles with a bound of its own: the fixture's settle gives up after its deadline while ten thousand Entries still move.
    private static async Task SettleLongAsync(RealMeshFixture mesh)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                await mesh.SettleAsync(None).ConfigureAwait(false);
                return;
            }
            catch (TestMeshException) when (watch.Elapsed < TimeSpan.FromMinutes(5))
            {
                // Still moving: settle again.
            }
        }
    }

    private static async Task<PackageId> SharedChannelAsync(RealMeshFixture mesh)
    {
        await mesh.PairAsync(Alice, Bob, None).ConfigureAwait(false);
        await mesh.ConnectAsync(Alice, Bob, None).ConfigureAwait(false);
        await mesh.HoldUserKeyAsync(Alice, None).ConfigureAwait(false);
        PackageId channel;
        await using (LocalClient owner = await mesh.ConnectUserAsync(Alice, "bench", None).ConfigureAwait(false))
        {
            channel = (await owner.SendAsync(new CreatePackage { Kind = PackageKind.Channel, Name = "Family" }, None).ConfigureAwait(false)).Value.PackageId;
            await owner.SendAsync(new SetGrant { PackageId = channel, GranteeId = TestIdentity.Of(Bob).UserId, Rights = GrantRights.Write }, None).ConfigureAwait(false);
        }

        await mesh.SettleAsync(None).ConfigureAwait(false);
        await using (LocalClient follower = await mesh.ConnectUserAsync(Bob, "bench", None).ConfigureAwait(false))
            await follower.SendAsync(new FollowPackage { PackageId = channel }, None).ConfigureAwait(false);
        await mesh.SettleAsync(None).ConfigureAwait(false);
        return channel;
    }

    private static async Task UntilAsync(RealMeshFixture mesh, string node, Func<LocalClient, Task<bool>> condition)
    {
        await using LocalClient app = await mesh.ConnectUserAsync(node, "bench", None).ConfigureAwait(false);
        var watch = Stopwatch.StartNew();
        while (!await condition(app).ConfigureAwait(false))
        {
            if (watch.Elapsed > TimeSpan.FromMinutes(5))
                throw new TimeoutException("The chat service did not get there within five minutes.");
            await Task.Yield();
        }
    }
}
