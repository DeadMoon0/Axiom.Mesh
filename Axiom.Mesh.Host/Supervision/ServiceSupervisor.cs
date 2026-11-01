using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Local;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Host;

/// <summary>
/// Keeps one spawned service running (LOCAL-SPEC.md §4.2, §9.1, debt 32): for every start a fresh token of 32 random bytes,
/// set as the name's token before the child starts and withdrawn as soon as it exited; the child started in its state
/// directory with its token, the pipe, the daemon's home and the state directory in its environment; its output into the
/// daemon's log; its exit judged by <see cref="SupervisionRule"/>. The token appears in no log line and no argument.
/// </summary>
internal sealed class ServiceSupervisor
{
    /// <summary>The variable a spawned service finds its state directory in (LOCAL-SPEC.md §4.2); the Service package names it too.</summary>
    public const string StateVariable = "AXIOM_MESH_SERVICE_STATE";

    private readonly SupervisedService service;
    private readonly LocalPort port;
    private readonly IChildProcesses processes;
    private readonly IEntropy entropy;
    private readonly TimeProvider time;
    private readonly ILogger log;
    private readonly CancellationTokenSource stopping = new();
    private Task running = Task.CompletedTask;
    private IChildProcess? current;
    private int killed;

    /// <summary>A supervisor, not yet started.</summary>
    /// <param name="service">What it supervises.</param>
    /// <param name="port">The node's local port, whose spawned token it sets and withdraws.</param>
    /// <param name="processes">Where children come from.</param>
    /// <param name="entropy">Where tokens come from.</param>
    /// <param name="time">The daemon's clock: run times and restart delays.</param>
    /// <param name="log">The daemon's log.</param>
    public ServiceSupervisor(SupervisedService service, LocalPort port, IChildProcesses processes, IEntropy entropy, TimeProvider time, ILogger log)
    {
        this.service = service;
        this.port = port;
        this.processes = processes;
        this.entropy = entropy;
        this.time = time;
        this.log = log;
    }

    /// <summary>The service's name.</summary>
    public string Name => service.Name;

    /// <summary>Starts the first child and keeps the service running until <see cref="StopAsync"/>.</summary>
    public void Start() => running = Task.Run(RunAsync, CancellationToken.None);

    /// <summary>The daemon stops: no child is started again, an exit from now on is no failure.</summary>
    public void MarkStopping() => stopping.Cancel();

    /// <summary>Waits up to <paramref name="grace"/> for the child to exit (the daemon's drain asks it to), then kills it.</summary>
    /// <param name="grace">How long the child may take.</param>
    /// <returns>A task that completes when the child is gone and the supervisor ended.</returns>
    public async Task StopAsync(TimeSpan grace)
    {
        MarkStopping();
        if (Volatile.Read(ref current) is { } child && !child.Exited.IsCompleted)
        {
            using var waited = new CancellationTokenSource();
            Task first = await Task.WhenAny(child.Exited, Task.Delay(grace, time, waited.Token)).ConfigureAwait(false);
            await waited.CancelAsync().ConfigureAwait(false);
            if (first != child.Exited)
            {
                Volatile.Write(ref killed, 1);
                HostLog.ServiceKilled(log, service.Name, grace.TotalMilliseconds);
                child.Kill();
            }
        }

        await running.ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        SupervisionState state = SupervisionState.Fresh;
        while (!stopping.IsCancellationRequested)
        {
            ChildExit exit = await RunChildAsync().ConfigureAwait(false);
            SupervisionDecision decision = SupervisionRule.Decide(state with { Stopping = stopping.IsCancellationRequested }, exit, time.GetUtcNow());
            state = decision.Next;
            HostLog.ServiceExited(log, service.Name, exit.ExitCode, decision.Why);
            switch (decision.Action)
            {
                case SupervisionAction.Restart:
                    try
                    {
                        await Task.Delay(decision.Delay, time, stopping.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    break;
                case SupervisionAction.GiveUp:
                    HostLog.ServiceGaveUp(log, service.Name, decision.Why);
                    return;
                default:
                    return;
            }
        }
    }

    // One child, from its token to its exit. The token is withdrawn before anything else once the child is gone.
    private async Task<ChildExit> RunChildAsync()
    {
        long started = time.GetTimestamp();
        port.SetSpawnedToken(service.Name, MintToken(out string hex));
        try
        {
            HostFiles.CreatePrivateDirectory(service.StateDirectory);
            IChildProcess child = processes.Start(new ChildStart
            {
                Executable = service.Executable,
                Arguments = service.Arguments,
                WorkingDirectory = service.StateDirectory,
                Environment = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ServiceToken.EnvironmentVariable] = hex,
                    [PipeLocation.EnvironmentVariable] = service.Pipe,
                    [HostPaths.HomeVariable] = service.Home,
                    [StateVariable] = service.StateDirectory,
                },
            });
            using (child)
            {
                Volatile.Write(ref current, child);
                HostLog.ServiceStarted(log, service.Name, child.Id, service.Executable);
                _ = PumpAsync(child);
                int code = await child.Exited.ConfigureAwait(false);
                Volatile.Write(ref current, null);
                return new ChildExit(code, time.GetElapsedTime(started), Interlocked.Exchange(ref killed, 0) == 1);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            HostLog.ServiceNotStarted(log, service.Name, service.Executable, ex.Message);
            return new ChildExit(-1, time.GetElapsedTime(started), Killed: false);
        }
        finally
        {
            port.ClearSpawnedToken(service.Name);
        }
    }

    private async Task PumpAsync(IChildProcess child)
    {
        try
        {
            await foreach (string line in child.Output.ReadAllAsync().ConfigureAwait(false))
                HostLog.ServiceOutput(log, service.Name, line);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The child is gone.
        }
    }

    private ServiceToken MintToken(out string hex)
    {
        Span<byte> bytes = stackalloc byte[Limits.ServiceTokenLength];
        entropy.Fill(bytes);
        hex = Convert.ToHexString(bytes).ToLowerInvariant();
        ServiceToken token = ServiceToken.FromBytes(bytes);
        bytes.Clear();
        return token;
    }
}
