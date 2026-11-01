using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Cli;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Integration.Tests.Rules;

/// <summary>
/// Scenario 14 of the chat suite (plan §9.3 item 5; LOCAL-SPEC.md §4.2, §9.1; S-D1): the real daemon, as its own process,
/// spawns the chat service as the program of its own the build put beside the service's assembly, supervises it, and
/// starts it again when it is killed; the command line reaches it through the daemon's pipe throughout.
/// </summary>
public sealed class ChatProcessTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §4.2, §9.1: the daemon spawns the chat service (the apphost of Axiom.Mesh.Chat, as axiom-mesh-chat is published) with a
    // token, the pipe and a state directory; `chat list` works; killed from outside, the service is started again as a new
    // process and `chat list` works again, its index kept in the state directory.
    [Fact]
    public async Task TheDaemonSpawnsTheChatService_RestartsItWhenKilled_AndChatListWorksThroughout()
    {
        string home = Path.Combine(Path.GetTempPath(), "axiom-mesh-it", "chat-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(Path.Combine(home, "config.json"), $$"""
            { "mesh": { "port": 0, "listen": "127.0.0.1", "mdns": false, "broadcast": false },
              "services": { "chat": { "spawn": {{JsonSerializer.Serialize(Apphost())}}, "kinds": [2], "entryKinds": [2, 3] } } }
            """);
        try
        {
            await using (DaemonProcess daemon = DaemonProcess.Start(home))
            {
                int first = PidOf(await daemon.WaitForAsync("service chat started: pid", 1));
                await using (LocalClient app = await ConnectAsync(daemon.Pipe))
                    Assert.True((await app.SendAsync(new CreateIdentity { UserName = "Alice", DeviceName = "laptop" }, None)).IsOk);
                CliResult before = await ChatListAsync(daemon.Pipe);

                using (Process child = Process.GetProcessById(first))
                    child.Kill(entireProcessTree: true);
                await daemon.WaitForAsync("service chat exited with", 1);
                int second = PidOf(await daemon.WaitForAsync("service chat started: pid", 2));
                CliResult after = await ChatListAsync(daemon.Pipe);

                Assert.NotEqual(first, second);
                Assert.Contains("no channels", before.Stdout, StringComparison.Ordinal);
                Assert.Contains("no channels", after.Stdout, StringComparison.Ordinal);
                Assert.True(Directory.Exists(Path.Combine(home, "services", "chat", "index")));
                Assert.DoesNotContain(daemon.Lines, line => line.Contains("is given up", StringComparison.Ordinal));
            }
        }
        finally
        {
            try
            {
                Directory.Delete(home, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file still held by a closing handle; the system's temporary folder is cleaned eventually.
            }
        }
    }

    // The service's own program as the build leaves it beside its assembly: Axiom.Mesh.Chat(.exe), a framework-dependent apphost.
    private static string Apphost()
    {
        string assembly = typeof(ChatProgram).Assembly.Location;
        string apphost = Path.Combine(Path.GetDirectoryName(assembly)!, Path.GetFileNameWithoutExtension(assembly) + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        Assert.True(File.Exists(apphost), $"The build left no apphost at {apphost}.");
        return apphost;
    }

    // The daemon may still be opening its pipe: try again until it answers, within the deadline.
    private static async Task<LocalClient> ConnectAsync(PipeLocation pipe)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        while (true)
        {
            try
            {
                return await LocalClient.OpenAsync(await LocalPipe.ConnectAsync(pipe, deadline.Token), new LocalClientOptions { Name = "chat-process-test" }, deadline.Token);
            }
            catch (Exception ex) when (ex is IOException or LocalConnectionException or TimeoutException && !deadline.IsCancellationRequested)
            {
                await Task.Yield();
            }
        }
    }

    // `chat list` through the pipe, again while the restarted service is still connecting (ServiceUnavailable, LOCAL-SPEC.md §6.7).
    private static async Task<CliResult> ChatListAsync(PipeLocation pipe)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        while (true)
        {
            var environment = new CliEnvironment
            {
                Version = "integration",
                HostAddress = pipe.Path,
                ConnectHost = ct => LocalPipe.ConnectAsync(pipe, ct),
                CurrentDirectory = Path.GetTempPath(),
            };
            using var output = new StringWriter();
            using var error = new StringWriter();
            CliExitCode exit = await CliProgram.RunAsync(["chat", "list"], output, error, environment, None);
            var result = new CliResult(exit, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
            if (exit == CliExitCode.Ok)
                return result;
            Assert.True(result.Stderr.Contains("chat service is not connected", StringComparison.Ordinal), "chat list failed otherwise: " + result);
            Assert.False(deadline.IsCancellationRequested, "chat list did not succeed within the deadline: " + result);
            await Task.Yield();
        }
    }

    private static int PidOf(string line) => int.Parse(Regex.Match(line, @"pid (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
}
