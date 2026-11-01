using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Axiom.Mesh.Host;

/// <summary>
/// The operating system's processes as <see cref="IChildProcesses"/>: started without a shell or a window, the token passed
/// in the environment only (never an argument), standard input closed, standard output and error read line by line, killed
/// with every process the child started.
/// </summary>
internal sealed class SystemChildProcesses : IChildProcesses
{
    /// <inheritdoc />
    public bool Exists(string executable) => File.Exists(executable);

    /// <inheritdoc />
    public IChildProcess Start(ChildStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var info = new ProcessStartInfo(start.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = start.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in start.Arguments)
            info.ArgumentList.Add(argument);
        foreach ((string name, string value) in start.Environment)
            info.Environment[name] = value;

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var child = new Child(process);
        if (!process.Start())
            throw new IOException($"{start.Executable} did not start.");
        process.StandardInput.Close();
        child.Begin();
        return child;
    }

    private sealed class Child : IChildProcess
    {
        private readonly Process process;
        private readonly Channel<string> output = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        private readonly TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int open = 2;

        public Child(Process process)
        {
            this.process = process;
            process.OutputDataReceived += (_, e) => Line(e.Data);
            process.ErrorDataReceived += (_, e) => Line(e.Data);
            process.Exited += (_, _) => _ = ExitAsync();
        }

        public int Id => process.Id;

        public ChannelReader<string> Output => output.Reader;

        public Task<int> Exited => exited.Task;

        public void Begin()
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (process.HasExited)
                _ = ExitAsync();
        }

        public void Kill()
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // It exited meanwhile, or the system refused: the exit task reports what happened.
            }
        }

        public void Dispose() => process.Dispose();

        // A null line is the end of one stream; the channel completes when both ended.
        private void Line(string? line)
        {
            if (line is not null)
                output.Writer.TryWrite(line);
            else if (System.Threading.Interlocked.Decrement(ref open) == 0)
                output.Writer.TryComplete();
        }

        private async Task ExitAsync()
        {
            // WaitForExitAsync also waits until the redirected streams are read to their end.
            await process.WaitForExitAsync().ConfigureAwait(false);
            exited.TrySetResult(process.ExitCode);
        }
    }
}
