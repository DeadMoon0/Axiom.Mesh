using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Host.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>
/// <c>axiom-meshd run</c> (fundamentals plan §2.6): it starts with nothing configured, prints where its home, pipe and
/// mesh endpoint are, answers on its pipe, and on the stop signal (Ctrl-C or SIGTERM) drains its clients and exits 0; a
/// configuration it cannot use is one message with the steps out and exit code 2, never a stack trace.
/// </summary>
public sealed class RunCommandTests
{
    [Fact]
    public async Task RunStartsWithNothingConfigured_SaysWhereEverythingIs_AnswersOnThePipe_AndStopsWhenTold()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            return;

        using var home = new TempHome();
        await File.WriteAllTextAsync(Path.Combine(home.Path, "config.json"), "{ \"mesh\": { \"port\": 0, \"listen\": \"127.0.0.1\", \"mdns\": false, \"broadcast\": false } }");
        string pipe = OperatingSystem.IsWindows() ? "axiom-mesh-run-" + Guid.NewGuid().ToString("N")[..12] : Path.Combine(home.Path, "run.sock");
        var output = new SynchronizedWriter();
        var error = new StringWriter();
        using var stop = new CancellationTokenSource();

        Task<HostExitCode> running = HostProgram.RunAsync(
            ["run", "--home", home.Path], output, error, "0.1.0-test",
            name => name == PipeLocation.EnvironmentVariable ? pipe : Environment.GetEnvironmentVariable(name),
            stop.Token);
        await UntilAsync(() => output.Text.Contains("Press Ctrl-C", StringComparison.Ordinal) || running.IsCompleted, "the daemon to say it runs");
        Assert.False(running.IsCompleted, error.ToString());

        string said = output.Text;
        Assert.Contains("home      " + home.Path, said, StringComparison.Ordinal);
        Assert.Contains("pipe      ", said, StringComparison.Ordinal);
        Assert.Contains("mesh      127.0.0.1:", said, StringComparison.Ordinal);
        Assert.Contains("identity  none yet", said, StringComparison.Ordinal);

        PipeLocation location = PipeLocation.Resolve(HostPlatform.Current, new PipeEnvironment { Pipe = pipe });
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            await using LocalClient client = await LocalClient.OpenAsync(await LocalPipe.ConnectAsync(location, deadline.Token), new LocalClientOptions { Name = "run-test" }, deadline.Token);
            Assert.True((await client.SendAsync(new GetIdentity(), deadline.Token)).IsOk);
        }

        await stop.CancelAsync();
        Assert.Equal(HostExitCode.Ok, await running.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Contains("stops", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConfigurationItCannotUseIsOneMessageWithTheStepsOut_ExitCode2()
    {
        using var home = new TempHome();
        await File.WriteAllTextAsync(Path.Combine(home.Path, "config.json"), "{ \"mesh\": { \"unknown\": 1 } }");
        var output = new StringWriter();
        var error = new StringWriter();

        HostExitCode exit = await HostProgram.RunAsync(["run", "--home", home.Path], output, error, "0.1.0-test", _ => null, CancellationToken.None);

        Assert.Equal(HostExitCode.Configuration, exit);
        Assert.Contains("unknown", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error.ToString(), StringComparison.Ordinal);
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
