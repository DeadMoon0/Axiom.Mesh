using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.Mesh.Core;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Discovery;
using Axiom.Mesh.Host;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;
using Axiom.Mesh.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axiom.Mesh.Integration.Tests.Support;

/// <summary>
/// One real node of a <see cref="RealMeshFixture"/>: a <see cref="MeshNode"/> that binds a TCP listener on its own loopback
/// address, keeps its objects and its index in a directory of its own, dials with the real TCP dialler, and serves its
/// local port on a real pipe through the daemon's <see cref="PipeHost"/>. Its keys live in memory and survive its restarts;
/// everything else it keeps on disk.
/// </summary>
internal sealed class RealNode
{
    private readonly InMemoryNetwork board;
    private readonly List<LocalClient> clients = [];
    private readonly List<RunningService> services = [];
    private PipeHost? pipe;
    private Database? database;
    private MeshNode? node;
    private bool adopted;

    public RealNode(TestIdentity identity, IPAddress address, string directory, InMemoryNetwork board, bool withIdentity, LocalPortOptions local)
    {
        Local = local;
        Identity = identity;
        Address = address;
        Directory = directory;
        this.board = board;
        Keys = InMemoryKeyStore.For(identity);
        adopted = !withIdentity;
        string pipeName = "axiom-mesh-it-" + Guid.NewGuid().ToString("N")[..12];
        Pipe = PipeLocation.Resolve(
            OperatingSystem.IsWindows() ? LocalPlatform.Windows : LocalPlatform.Linux,
            new PipeEnvironment { Pipe = OperatingSystem.IsWindows() ? pipeName : System.IO.Path.Combine(directory, "pipe.sock") });
    }

    public string Name => Identity.Name;

    /// <summary>The local port's configuration: <c>cli</c> and the scenario's services, all external.</summary>
    public LocalPortOptions Local { get; }

    public TestIdentity Identity { get; }

    /// <summary>The loopback address the node listens and announces at: 127.0.0.n, one per node.</summary>
    public IPAddress Address { get; }

    public string Directory { get; }

    public InMemoryKeyStore Keys { get; }

    public PipeLocation Pipe { get; }

    public FakeDiscovery? Discovery { get; private set; }

    public bool IsRunning => node is not null;

    public MeshNode Node => node ?? throw new InvalidOperationException($"{Name} is stopped; start it with StartNodeAsync.");

    /// <summary>Opens the pipe, the index and the store, starts the node on a fresh port, and serves the pipe.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (node is not null)
            return;
        var endpoint = new IPEndPoint(Address, FreePort(Address));
        Discovery = new FakeDiscovery(board, new Announcement(Identity.DeviceId, Identity.UserId, endpoint, DiscoverySource.Mdns));
        pipe = PipeHost.Open(Pipe, [], TimeProvider.System, NullLogger.Instance);
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Directory, "data"));
            database = await Database.OpenAsync(System.IO.Path.Combine(Directory, "index"), DatabaseOptions.Default, cancellationToken).ConfigureAwait(false);
            var environment = new MeshNodeEnvironment
            {
                Files = new DirectoryObjectFiles(System.IO.Path.Combine(Directory, "data"), new SystemEntropy()),
                Database = database,
                Keys = Keys,
                Discovery = new CompositeDiscovery(new CompositeDiscoveryOptions
                {
                    Parts = [Discovery],
                    Time = TimeProvider.System,
                    Cache = new AnnouncementCache(256, TimeSpan.Zero),
                }),
                Dialer = new TcpDialer(TimeProvider.System),
                Time = TimeProvider.System,
                Entropy = new SystemEntropy(),
                Loggers = NullLoggerFactory.Instance,
                Addresses = () => [Address],
            };
            MeshNodeOptions options = MeshNodeOptions.Default with
            {
                MeshPort = endpoint.Port,
                ListenAddress = Address,
                DeviceName = Name,
                Local = Local with { HostUser = pipe.HostUser },
            };
            node = await MeshNode.StartAsync(options, environment, cancellationToken).ConfigureAwait(false);
            pipe.Serve(node);
            if (!adopted)
            {
                await node.AdoptIdentityAsync(TestIdentity.RosterOf(Identity.UserName), Identity.UserName, cancellationToken).ConfigureAwait(false);
                adopted = true;
            }
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Says Bye on every client handed out, stops the node, removes the pipe and closes the index.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Discovery?.Withdraw();
        LocalClient[] open;
        lock (clients)
        {
            open = [.. clients];
            clients.Clear();
        }

        foreach (LocalClient client in open)
            await client.DisposeAsync().ConfigureAwait(false);
        RunningService[] started;
        lock (services)
        {
            started = [.. services];
            services.Clear();
        }

        foreach (RunningService service in started)
            await service.DisposeAsync().ConfigureAwait(false);
        if (Interlocked.Exchange(ref node, null) is { } running)
            await running.StopAsync(cancellationToken).ConfigureAwait(false);
        if (Interlocked.Exchange(ref pipe, null) is { } open2)
            await open2.DisposeAsync().ConfigureAwait(false);
        if (Interlocked.Exchange(ref database, null) is { } index)
            await index.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>A client over the real pipe, Hello said; closed when the node stops.</summary>
    public async Task<LocalClient> ConnectAsync(LocalClientOptions options, CancellationToken cancellationToken)
    {
        Stream stream = await LocalPipe.ConnectAsync(Pipe, cancellationToken).ConfigureAwait(false);
        LocalClient client = await LocalClient.OpenAsync(stream, options, cancellationToken).ConfigureAwait(false);
        lock (clients)
            clients.Add(client);
        return client;
    }

    /// <summary>
    /// Runs a service in process over the real pipe, as a program the user started would: its token from the configuration's
    /// text, its state in a real LDB directory of this node (<c>services/&lt;name&gt;/index</c>) kept across runs and restarts,
    /// the system's clock. Returns once it is connected.
    /// </summary>
    public async Task<RunningService> RunServiceAsync(IMeshService service, CancellationToken cancellationToken)
    {
        string name = service.Definition.Name;
        string state = System.IO.Path.Combine(Directory, "services", name);
        System.IO.Directory.CreateDirectory(state);
        RunningService running = RunningService.Start(service, new ServiceEnvironment
        {
            Version = "integration",
            ConnectHost = ct => LocalPipe.ConnectAsync(Pipe, ct),
            HostAddress = Pipe.ToString(),
            OpenState = ct => Database.OpenAsync(System.IO.Path.Combine(state, "index"), DatabaseOptions.Default, ct),
            StateDirectory = state,
            ReadConfig = () => TestServices.ConfigOf(name),
            ConfigFile = $"the configuration of {Name}",
        });
        try
        {
            await running.WhenConnectedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await running.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        lock (services)
            services.Add(running);
        return running;
    }

    // A port the system hands out now on this address; the node binds it a moment later. The listener is closed before
    // the node binds, so the port is free unless another process takes it in between, which the bind then reports.
    private static int FreePort(IPAddress address)
    {
        using var probe = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        probe.Bind(new IPEndPoint(address, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    public override string ToString() => $"{Name} at {Address}";
}
