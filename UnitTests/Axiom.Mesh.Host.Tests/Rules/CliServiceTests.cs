using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Host.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §4.2 (external services, plan D4) and §9 end to end on a running daemon and its real pipe: the default
/// configuration lists <c>cli</c> as external with a token the daemon writes into <c>config.json</c> on its first start; a
/// program that presents that token is the <c>cli</c> service, registers the Channel kind, writes a Message Entry the user
/// role then reads back, and puts a file with the client's helper whose ChunkList the Entry's envelope references.
/// </summary>
public sealed class CliServiceTests
{
    [Fact]
    public async Task TheCliExternalServiceWithTheTokenFromConfigJsonRegistersWritesAnEntryAndPutsAFile()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            return;

        using var home = new TempHome();
        await File.WriteAllTextAsync(Path.Combine(home.Path, "config.json"), "{ \"mesh\": { \"port\": 0, \"listen\": \"127.0.0.1\", \"mdns\": false, \"broadcast\": false } }");
        string pipe = OperatingSystem.IsWindows() ? "axiom-mesh-cli-" + Guid.NewGuid().ToString("N")[..12] : Path.Combine(home.Path, "cli.sock");
        var output = new SynchronizedWriter();
        using var stop = new CancellationTokenSource();
        Task<HostExitCode> running = HostProgram.RunAsync(
            ["run", "--home", home.Path], output, TextWriter.Null, "0.1.0-test",
            name => name == PipeLocation.EnvironmentVariable ? pipe : Environment.GetEnvironmentVariable(name),
            stop.Token);
        await UntilAsync(() => output.Text.Contains("Press Ctrl-C", StringComparison.Ordinal) || running.IsCompleted, "the daemon to run");
        Assert.False(running.IsCompleted);
        PipeLocation location = PipeLocation.Resolve(HostPlatform.Current, new PipeEnvironment { Pipe = pipe });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        CancellationToken token = deadline.Token;

        await using (LocalClient user = await LocalClient.OpenAsync(await LocalPipe.ConnectAsync(location, token), new LocalClientOptions { Name = "cli-test" }, token))
        {
            Assert.True((await user.SendAsync(new CreateIdentity { UserName = "Alice", DeviceName = "laptop" }, token)).IsOk);
            Package channel = (await user.SendAsync(new CreatePackage { Kind = PackageKind.Channel, Name = "Family" }, token)).Value;
            string file = Path.Combine(home.Path, "note.txt");
            await File.WriteAllTextAsync(file, "a note");

            ServiceToken cliToken = TokenOf(Path.Combine(home.Path, "config.json"));
            await using LocalClient cli = await LocalClient.OpenAsync(
                await LocalPipe.ConnectAsync(location, token),
                new LocalClientOptions { Role = Role.Service, Name = "cli", Token = cliToken },
                token);
            Assert.Equal(Role.Service, cli.Host.Role);
            Assert.True((await cli.SendAsync(new RegisterKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message, EntryKind.Redaction] }, token)).IsOk);
            StoredFile stored = (await StoreFiles.PutFileAsync(cli, file, null, token)).Value;
            var message = new MessagePayload { Text = "hello from the cli", Attachments = [new Attachment("note.txt", stored.ChunkList, stored.Size)], ReplyTo = default };
            Entry written = (await cli.SendAsync(WriteEntry.Of(channel.PackageId, message.ToBody()), token)).Value;

            EntryPage history = (await user.SendAsync(new ListHistory { PackageId = channel.PackageId, Limit = 10 }, token)).Value;
            Assert.Equal(written.Hash, Assert.Single(history.Entries).Hash);
            Assert.Equal((0ul, 1ul), (written.Clock, written.StoreSeq));
            Assert.Equal([stored.ChunkList], written.References);
            Assert.Equal(6ul, stored.Size);
        }

        await stop.CancelAsync();
        Assert.Equal(HostExitCode.Ok, await running.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    // The token the daemon wrote for the cli service: 64 lowercase hex characters (LOCAL-SPEC.md §4.2).
    private static ServiceToken TokenOf(string config)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(config));
        string hex = document.RootElement.GetProperty("services").GetProperty("cli").GetProperty("token").GetString()!;
        Assert.True(ServiceToken.TryParseHex(hex, out ServiceToken token));
        return token;
    }

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException($"Waited 30 s for {what}, in vain.");
            await Task.Yield();
        }
    }
}
