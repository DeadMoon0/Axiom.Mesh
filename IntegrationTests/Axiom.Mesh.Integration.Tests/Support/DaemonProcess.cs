using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Integration.Tests.Support;

/// <summary>
/// The real daemon as its own process: the <c>Axiom.Mesh.Host</c> apphost the build puts beside this suite, started with
/// <c>run --home</c> on a pipe of its own, its console log read line by line. Disposing it kills it with every process it
/// spawned, so no service outlives a test.
/// </summary>
internal sealed class DaemonProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly ConcurrentQueue<string> lines = new();

    private DaemonProcess(Process process, PipeLocation pipe)
    {
        this.process = process;
        Pipe = pipe;
        process.OutputDataReceived += (_, e) => Add(e.Data);
        process.ErrorDataReceived += (_, e) => Add(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>Where its pipe is.</summary>
    public PipeLocation Pipe { get; }

    /// <summary>Every line it logged so far.</summary>
    public string[] Lines => [.. lines];

    /// <summary>Starts the daemon on a home directory whose <c>config.json</c> is written already.</summary>
    /// <param name="home">The home directory.</param>
    /// <returns>The running daemon.</returns>
    public static DaemonProcess Start(string home)
    {
        string daemon = Path.Combine(AppContext.BaseDirectory, "Axiom.Mesh.Host" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        Assert.True(File.Exists(daemon), $"The build left no daemon at {daemon}.");
        LocalPlatform platform = OperatingSystem.IsWindows() ? LocalPlatform.Windows : OperatingSystem.IsMacOS() ? LocalPlatform.MacOS : LocalPlatform.Linux;
        string pipe = OperatingSystem.IsWindows() ? "axiom-mesh-it-" + Guid.NewGuid().ToString("N")[..12] : Path.Combine(home, "daemon.sock");
        var start = new ProcessStartInfo(daemon)
        {
            ArgumentList = { "run", "--home", home },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = home,
        };
        start.Environment[PipeLocation.EnvironmentVariable] = pipe;
        Process process = Process.Start(start) ?? throw new InvalidOperationException($"{daemon} did not start.");
        return new DaemonProcess(process, PipeLocation.Resolve(platform, new PipeEnvironment { Pipe = pipe }));
    }

    /// <summary>Waits until <paramref name="count"/> lines contain <paramref name="text"/>, within the mesh's deadline of real time.</summary>
    /// <param name="text">The text.</param>
    /// <param name="count">How many such lines.</param>
    /// <returns>The last such line.</returns>
    public async Task<string> WaitForAsync(string text, int count)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        while (true)
        {
            string[] matching = [.. lines.Where(l => l.Contains(text, StringComparison.Ordinal))];
            if (matching.Length >= count)
                return matching[^1];
            if (deadline.IsCancellationRequested || process.HasExited)
                throw new TimeoutException($"Fewer than {count} lines of the daemon contained \"{text}\". Its log: {string.Join(" | ", lines.TakeLast(30))}");
            await Task.Yield();
        }
    }

    /// <summary>Kills the daemon and every process it spawned, and waits until it exited.</summary>
    /// <returns>A task that completes when it exited.</returns>
    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        process.Dispose();
    }

    private void Add(string? line)
    {
        if (line is not null)
            lines.Enqueue(line);
    }
}
