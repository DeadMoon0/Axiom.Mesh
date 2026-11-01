using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Axiom.Mesh.Host;

/// <summary>One running spawned service: its process id, its output lines (standard output and error), its exit and a kill.</summary>
internal interface IChildProcess : IDisposable
{
    /// <summary>The process id, for the log.</summary>
    int Id { get; }

    /// <summary>Every line the child writes to standard output or standard error, in the order read; completes when both closed.</summary>
    ChannelReader<string> Output { get; }

    /// <summary>Completes with the exit code when the child exited.</summary>
    Task<int> Exited { get; }

    /// <summary>Kills the child and every process it started (the daemon's stop, after the grace).</summary>
    void Kill();
}
