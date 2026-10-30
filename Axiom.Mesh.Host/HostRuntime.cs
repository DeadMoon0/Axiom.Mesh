using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
/// (LOCAL-SPEC.md §2.2) and hands to the node's local port. The pipe opens first, so a daemon whose pipe is taken
/// touches nothing of the store another one holds.
/// </summary>
internal sealed class HostRuntime : IAsyncDisposable
{
    private readonly IPipeListener listener;
    private readonly Database database;
    private readonly IdentityGatedDiscovery gated;
    private readonly ILoggerFactory loggers;
    private readonly ILogger log;
    private readonly CancellationTokenSource stopping = new();
    private readonly List<Task> loops = [];
    private int stopped;

    private HostRuntime(HostPaths paths, HostOptions options, IPipeListener listener, Database database, IdentityGatedDiscovery gated, MeshNode node, ILoggerFactory loggers)
    {
        Paths = paths;
        Options = options;
        this.listener = listener;
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
    public string Pipe => listener.Location;

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
            if (start.ConsoleLog)
                builder.AddSimpleConsole(console => { console.SingleLine = true; console.TimestampFormat = "HH:mm:ss "; });
        });

        IPipeListener? listener = null;
        Database? database = null;
        IdentityGatedDiscovery? gated = null;
        try
        {
            listener = PipeListeners.Open(PipeLocationOf(start.Environment, options), options.Pipe.AllowUsers, start.Time);
            HostFiles.CreatePrivateDirectory(paths.Data);
            database = await Database.OpenAsync(Path.Combine(paths.Data, "index"), DatabaseOptions.Default with { TimeProvider = start.Time }, cancellationToken).ConfigureAwait(false);

            var facts = new DiscoveryFacts(256);
            var self = new TaskCompletionSource<SelfRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
            gated = new IdentityGatedDiscovery(ct => self.Task.WaitAsync(ct), options.Mesh.Mdns, options.Mesh.Broadcast, start.Time, facts);
            List<IDiscovery> parts = [gated];
            if (options.Mesh.StaticPeers.Count != 0)
            {
                EndPoint[] peers = [.. options.Mesh.StaticPeers.Select(p => HostConfiguration.TryParseEndPoint(p, out EndPoint e) ? e : throw new InvalidOperationException(p))];
                parts.Add(new StaticPeers(new StaticPeersOptions { Peers = peers, Time = start.Time, Facts = facts }));
            }

            var environment = new MeshNodeEnvironment
            {
                Files = new DirectoryObjectFiles(paths.Data, start.Entropy),
                Database = database,
                Keys = keys,
                Discovery = new CompositeDiscovery(new CompositeDiscoveryOptions { Parts = parts, Time = start.Time, Cache = new AnnouncementCache(1024, TimeSpan.Zero) }),
                Dialer = new TcpDialer(start.Time),
                Time = start.Time,
                Entropy = start.Entropy,
                Loggers = loggers,
            };
            MeshNode node = await MeshNode.StartAsync(HostConfiguration.NodeOptionsOf(options, listener.HostUser, start.Version), environment, cancellationToken).ConfigureAwait(false);
            var runtime = new HostRuntime(paths, options, listener, database, gated, node, loggers);
            runtime.Note(listener);
            runtime.loops.Add(Task.Run(() => runtime.AcceptAsync(runtime.stopping.Token), CancellationToken.None));
            runtime.loops.Add(Task.Run(() => runtime.LogDiscoveryAsync(facts, runtime.stopping.Token), CancellationToken.None));
            runtime.loops.Add(Task.Run(() => runtime.AnnounceWhenIdentifiedAsync(self, runtime.stopping.Token), CancellationToken.None));
            HostLog.Started(runtime.log, start.Version, paths.Home, listener.Location, node.MeshEndpoint.ToString());
            return runtime;
        }
        catch
        {
            if (gated is not null)
                await gated.DisposeAsync().ConfigureAwait(false);
            if (database is not null)
                await database.DisposeAsync().ConfigureAwait(false);
            if (listener is not null)
                await listener.DisposeAsync().ConfigureAwait(false);
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
            Pipe = environment(PipeLocation.EnvironmentVariable) is { Length: > 0 } variable ? variable : options.Pipe.Name,
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
        await listener.DisposeAsync().ConfigureAwait(false);
        await Node.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await gated.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(loops).ConfigureAwait(false);
        await database.DisposeAsync().ConfigureAwait(false);
        loggers.Dispose();
        stopping.Dispose();
    }

    // §2.2: the process identity decides before Hello is answered; a refused connection is closed without a frame.
    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            PipeConnection connection;
            try
            {
                connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or System.Threading.Channels.ChannelClosedException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
            {
                log.LogWarning(ex, "The pipe failed to accept a connection; it goes on accepting.");
                continue;
            }

            PipeAdmissionVerdict verdict = PipeAdmission.Decide(connection.Identity, listener.HostUser, Options.Pipe.AllowUsers, listener.RunningAsService);
            if (!verdict.IsAdmitted)
            {
                HostLog.PipeRefused(log, connection.Identity.Pid, connection.Identity.Executable, connection.Identity.User, verdict.Refusal!);
                await connection.Stream.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _ = Node.Local.AdoptAsync(connection.Stream, connection.Identity, cancellationToken);
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

    private void Note(IPipeListener pipe)
    {
        if (Options.Pipe.AllowUsers.Count != 0 && !pipe.RunningAsService)
            HostLog.ConfigurationNote(log, "pipe.allowUsers applies to a daemon that runs as a system service; this one runs as a user and admits that user alone.");
        foreach ((string name, ServiceSection service) in Options.Services)
        {
            if (service.Spawn is not null)
                HostLog.ConfigurationNote(log, $"services.{name}: this version does not spawn services yet; start it yourself, or list it as external with a token.");
            if (service.InProcess)
                HostLog.ConfigurationNote(log, $"services.{name}: no in-process service is compiled into this version; the name stays unused.");
        }
    }
}
