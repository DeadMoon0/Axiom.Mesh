using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.Mesh.Core;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Discovery;
using Axiom.Mesh.Local;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Host;

/// <summary>
/// The running daemon (fundamentals plan §2.6, DESIGN.md §10.2): configuration, keys, the store and database on disk,
/// discovery, the TCP dialler, the node, and the pipe, whose connections it admits by the process identity of each
/// (LOCAL-SPEC.md §2.2) and hands to the node's local port, and the services it spawns and supervises (§4.2, §9.1). The
/// pipe opens first, so a daemon whose pipe is taken touches nothing of the store another one holds; the services start
/// once the pipe serves, and stop last.
/// </summary>
internal sealed class HostRuntime : IAsyncDisposable
{
    private readonly PipeHost pipe;
    private readonly Database database;
    private readonly IdentityGatedDiscovery gated;
    private readonly ILoggerFactory loggers;
    private readonly ILogger log;
    private readonly CancellationTokenSource stopping = new();
    private readonly List<Task> loops = [];
    private readonly List<ServiceSupervisor> supervisors = [];
    private TimeSpan serviceGrace = LocalPortOptions.Default.DrainGrace;
    private int stopped;

    private HostRuntime(HostPaths paths, HostOptions options, PipeHost pipe, Database database, IdentityGatedDiscovery gated, MeshNode node, ILoggerFactory loggers)
    {
        Paths = paths;
        Options = options;
        this.pipe = pipe;
        this.database = database;
        this.gated = gated;
        Node = node;
        this.loggers = loggers;
        log = loggers.CreateLogger("Axiom.Mesh.Host");
    }

    /// <summary>Where the daemon keeps everything.</summary>
    public HostPaths Paths { get; }

    /// <summary>The configuration it runs with.</summary>
    public HostOptions Options { get; }

    /// <summary>The node.</summary>
    public MeshNode Node { get; }

    /// <summary>Where clients connect.</summary>
    public string Pipe => pipe.Location;

    /// <summary>Starts the daemon.</summary>
    /// <param name="start">What it is started with.</param>
    /// <param name="cancellationToken">Cancels starting.</param>
    /// <returns>The running daemon.</returns>
    /// <exception cref="MeshConfigurationException">The configuration cannot be used.</exception>
    /// <exception cref="HostException">The pipe is taken, or the keys are not private.</exception>
    /// <exception cref="MeshStoreException">The store or database cannot be used.</exception>
    public static async Task<HostRuntime> StartAsync(HostStart start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        HostPaths paths = start.Paths;
        HostOptions options = await HostConfiguration.LoadAsync(paths.ConfigFile, start.Entropy, cancellationToken).ConfigureAwait(false);
        FileKeyStore keys = FileKeyStore.Open(paths.Keys);
        ILoggerFactory loggers = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new RollingFileLoggerProvider(paths.Logs, start.Time));
            if (start.AlsoLogTo is { } extra)
                builder.AddProvider(extra);
            if (start.ConsoleLog)
                builder.AddSimpleConsole(console => { console.SingleLine = true; console.TimestampFormat = "HH:mm:ss "; });
        });

        PipeHost? pipe = null;
        Database? database = null;
        IdentityGatedDiscovery? gated = null;
        try
        {
            pipe = PipeHost.Open(PipeLocationOf(start.Environment, options), options.Pipe.AllowUsers, start.Time, loggers.CreateLogger("Axiom.Mesh.Host"));
            HostFiles.CreatePrivateDirectory(paths.Data);
            database = await Database.OpenAsync(Path.Combine(paths.Data, "index"), DatabaseOptions.Default with { TimeProvider = start.Time }, cancellationToken).ConfigureAwait(false);

            var facts = new DiscoveryFacts(256);
            var self = new TaskCompletionSource<SelfRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
            gated = new IdentityGatedDiscovery(ct => self.Task.WaitAsync(ct), options.Mesh.Mdns, options.Mesh.Broadcast, start.Time, facts);
            List<IDiscovery> parts = [gated];

            var files = new DirectoryObjectFiles(paths.Data, start.Entropy);
            StoreDirectoryFlush flush = files.ProbeDirectoryFlush();
            var environment = new MeshNodeEnvironment
            {
                Files = files,
                Database = database,
                Keys = keys,
                Discovery = new CompositeDiscovery(new CompositeDiscoveryOptions { Parts = parts, Time = start.Time, Cache = new AnnouncementCache(1024, TimeSpan.Zero) }),
                Dialer = new TcpDialer(start.Time),
                Time = start.Time,
                Entropy = start.Entropy,
                Loggers = loggers,
            };
            MeshNode node = await MeshNode.StartAsync(HostConfiguration.NodeOptionsOf(options, pipe.HostUser, start.Version) with { DeviceName = Environment.MachineName }, environment, cancellationToken).ConfigureAwait(false);
            var runtime = new HostRuntime(paths, options, pipe, database, gated, node, loggers);
            runtime.Note(pipe);
            pipe.Serve(node);
            runtime.Supervise(start, pipe.Location);
            runtime.loops.Add(Task.Run(() => runtime.LogDiscoveryAsync(facts, runtime.stopping.Token), CancellationToken.None));
            runtime.loops.Add(Task.Run(() => runtime.AnnounceWhenIdentifiedAsync(self, runtime.stopping.Token), CancellationToken.None));
            HostLog.Started(runtime.log, start.Version, paths.Home, pipe.Location, node.MeshEndpoint.ToString());
            HostLog.StoreFlush(runtime.log, paths.Data, flush);
            return runtime;
        }
        catch
        {
            if (gated is not null)
                await gated.DisposeAsync().ConfigureAwait(false);
            if (database is not null)
                await database.DisposeAsync().ConfigureAwait(false);
            if (pipe is not null)
                await pipe.DisposeAsync().ConfigureAwait(false);
            loggers.Dispose();
            throw;
        }
    }

    /// <summary>The pipe a daemon listens on: <c>AXIOM_MESH_PIPE</c>, else <c>pipe.name</c>, else the platform's default (LOCAL-SPEC.md §2.1).</summary>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="options">The configuration.</param>
    /// <returns>The location.</returns>
    public static PipeLocation PipeLocationOf(Func<string, string?> environment, HostOptions options)
    {
        string? runtimeDirectory = environment("XDG_RUNTIME_DIR");
        return PipeLocation.Resolve(HostPlatform.Current, new PipeEnvironment
        {
            Pipe = environment(PipeLocation.EnvironmentVariable),
            ConfiguredName = options.Pipe.Name,
            XdgRuntimeDir = runtimeDirectory,
            TmpDir = environment("TMPDIR"),
            UserId = OperatingSystem.IsLinux() && string.IsNullOrEmpty(runtimeDirectory) ? HostPlatform.UserId : null,
        });
    }

    /// <summary>
    /// Stops the daemon: no new pipe connection, the local clients drained with their grace, the sessions closed, the
    /// store and database released.
    /// </summary>
    /// <returns>A task that completes when everything is released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
            return;
        HostLog.Stopping(log, LocalPortOptions.Default.DrainGrace.TotalMilliseconds);
        await stopping.CancelAsync().ConfigureAwait(false);

        // A child that exits from now on is not restarted; the node's drain tells each child to finish, and whatever is
        // left after the grace is killed. Children go last, so that their connections drain like any client's.
        foreach (ServiceSupervisor supervisor in supervisors)
            supervisor.MarkStopping();
        await pipe.DisposeAsync().ConfigureAwait(false);
        await Node.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await Task.WhenAll(supervisors.Select(s => s.StopAsync(serviceGrace))).ConfigureAwait(false);
        await gated.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(loops).ConfigureAwait(false);
        await database.DisposeAsync().ConfigureAwait(false);
        loggers.Dispose();
        stopping.Dispose();
    }

    // Every spawned service whose executable is there gets a supervisor; one whose executable is absent is noted once and
    // left alone (S-D11): a development build without the service executables stays quiet.
    private void Supervise(HostStart start, string pipeLocation)
    {
        serviceGrace = start.ServiceGrace;
        foreach ((string name, ServiceSection service) in Options.Services.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            if (service.Spawn is not { } spawn)
                continue;
            string executable = SpawnPathRule.Resolve(spawn, start.HostDirectory, HostPlatform.Current);
            if (!start.Processes.Exists(executable))
            {
                HostLog.ConfigurationNote(log, $"services.{name}: \"{executable}\" is not there, so the service is not started; install it beside axiom-meshd or set services.{name}.spawn.");
                continue;
            }

            var supervisor = new ServiceSupervisor(
                new SupervisedService(name, executable, [.. service.Args], Paths.ServiceState(name), pipeLocation, Paths.Home),
                Node.Local,
                start.Processes,
                start.Entropy,
                start.Time,
                log);
            supervisors.Add(supervisor);
            supervisor.Start();
        }
    }

    private async Task LogDiscoveryAsync(DiscoveryFacts facts, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (DiscoveryFact fact in facts.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                HostLog.Discovery(log, fact.ToString());
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    // mDNS and broadcast announce the device: they start once it has an identity and its mesh port is bound.
    private async Task AnnounceWhenIdentifiedAsync(TaskCompletionSource<SelfRecord> self, CancellationToken cancellationToken)
    {
        try
        {
            NodeIdentity identity = await Node.WhenIdentityAsync(cancellationToken).ConfigureAwait(false);
            self.TrySetResult(new SelfRecord { Device = identity.Device, User = identity.User, Port = (ushort)Node.MeshEndpoint.Port });
        }
        catch (OperationCanceledException)
        {
            self.TrySetCanceled(cancellationToken);
        }
    }

    private void Note(PipeHost pipe)
    {
        if (Options.Pipe.AllowUsers.Count != 0 && !pipe.RunningAsService)
            HostLog.ConfigurationNote(log, "pipe.allowUsers applies to a daemon that runs as a system service; this one runs as a user and admits that user alone.");
        foreach ((string name, ServiceSection service) in Options.Services)
        {
            if (service.InProcess)
                HostLog.ConfigurationNote(log, $"services.{name}: no in-process service is compiled into this version; the name stays unused.");
        }
    }
}
