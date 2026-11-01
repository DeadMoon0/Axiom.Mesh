using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Axiom.Mesh.Host.Tests.Support;

/// <summary>
/// Children the test controls instead of the operating system's: every start is recorded with how it was asked for, each
/// child writes the lines and exits with the code the test says, and a kill exits it with -1.
/// </summary>
internal sealed class FakeChildProcesses(params string[] present) : IChildProcesses
{
    private readonly HashSet<string> present = new(present, StringComparer.Ordinal);
    private readonly Channel<FakeChild> started = Channel.CreateUnbounded<FakeChild>();
    private readonly ConcurrentQueue<FakeChild> all = new();
    private int lastId = 1000;

    /// <summary>Every child started so far, oldest first.</summary>
    public FakeChild[] Started => [.. all];

    public bool Exists(string executable) => present.Contains(executable);

    public IChildProcess Start(ChildStart start)
    {
        var child = new FakeChild(Interlocked.Increment(ref lastId), start);
        all.Enqueue(child);
        started.Writer.TryWrite(child);
        return child;
    }

    /// <summary>Waits for the next child the daemon starts, within 30 seconds of real time.</summary>
    /// <returns>The child.</returns>
    public async Task<FakeChild> NextAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await started.Reader.ReadAsync(deadline.Token);
    }

    /// <summary>One child the test drives.</summary>
    internal sealed class FakeChild(int id, ChildStart start) : IChildProcess
    {
        private readonly Channel<string> output = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int kills;

        /// <summary>How it was started.</summary>
        public ChildStart Start { get; } = start;

        /// <summary>How often the daemon killed it.</summary>
        public int Kills => Volatile.Read(ref kills);

        public int Id { get; } = id;

        public ChannelReader<string> Output => output.Reader;

        public Task<int> Exited => exited.Task;

        /// <summary>The child writes a line to its standard output.</summary>
        /// <param name="line">The line.</param>
        public void Write(string line) => output.Writer.TryWrite(line);

        /// <summary>The child exits.</summary>
        /// <param name="code">Its exit code.</param>
        public void Exit(int code)
        {
            output.Writer.TryComplete();
            exited.TrySetResult(code);
        }

        public void Kill()
        {
            Interlocked.Increment(ref kills);
            Exit(-1);
        }

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
