using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Cli;
using Axiom.Mesh.Core;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Integration.Tests.Support;

/// <summary>
/// A mesh of real nodes in this process (<see cref="IMeshFixture"/>): each binds a TCP listener on its own loopback
/// address (node n at 127.0.0.n+1, a port the system chooses), speaks real TLS 1.3 and AMP/1 over real sockets, keeps its
/// store and index in a temporary directory of its own, and serves its local port on a real pipe with a unique name, which
/// the clients and the command line of a scenario connect to as they would to <c>axiom-meshd</c>.
/// </summary>
/// <remarks>
/// Discovery is the one thing not taken from the operating system: there is no mDNS or broadcast in CI, and a static
/// address names no device (SPEC.md §3.3), so the nodes announce their real endpoints on an in-process board, the claims
/// mDNS would carry; a severed pair never hears each other's claim. Waits re-check after every fact a node records, within
/// <see cref="TestMesh.Deadline"/> of real time; nothing sleeps.
/// </remarks>
internal sealed class RealMeshFixture : IMeshFixture
{
    private static readonly TimeSpan SettleRecheck = TimeSpan.FromMilliseconds(5);

    private readonly Dictionary<string, RealNode> nodes;
    private readonly string root;

    private RealMeshFixture(Dictionary<string, RealNode> nodes, string root)
    {
        this.nodes = nodes;
        this.root = root;
    }

    /// <inheritdoc />
    public TimeProvider Clock => TimeProvider.System;

    /// <summary>Starts the nodes, each a device of its own user unless it starts without an identity.</summary>
    /// <param name="names">Published device names.</param>
    /// <param name="withoutIdentity">The nodes that start without an identity.</param>
    /// <param name="severed">Pairs of nodes that never hear each other's announcement.</param>
    /// <param name="services">The services every node's local port lists as external, beside <c>cli</c>.</param>
    /// <param name="cancellationToken">Cancels starting.</param>
    /// <returns>The running mesh.</returns>
    public static async Task<RealMeshFixture> StartAsync(IReadOnlyList<string> names, IReadOnlyList<string> withoutIdentity, IReadOnlyList<(string A, string B)> severed, IReadOnlyList<ServiceDefinition> services, CancellationToken cancellationToken)
    {
        string root = Path.Combine(Path.GetTempPath(), "axiom-mesh-it", Guid.NewGuid().ToString("N")[..12]);
        var board = new InMemoryNetwork();
        var nodes = new Dictionary<string, RealNode>(StringComparer.Ordinal);
        for (int i = 0; i < names.Count; i++)
        {
            string directory = Path.Combine(root, names[i]);
            Directory.CreateDirectory(directory);
            nodes[names[i]] = new RealNode(TestIdentity.Of(names[i]), new IPAddress([127, 0, 0, (byte)(i + 2)]), directory, board, !withoutIdentity.Contains(names[i]), TestServices.With(TestCliService.Local, [.. services]));
        }

        var fixture = new RealMeshFixture(nodes, root);
        try
        {
            foreach ((string a, string b) in severed)
                board.Sever(nodes[a].Address, nodes[b].Address);
            foreach (RealNode node in nodes.Values)
                await node.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return fixture;
    }

    /// <summary>A node of the mesh.</summary>
    /// <param name="name">Its published device name.</param>
    /// <returns>The node.</returns>
    public RealNode this[string name] =>
        nodes.TryGetValue(name, out RealNode? node) ? node
        : throw new KeyNotFoundException($"The mesh has no node '{name}'; it has {string.Join(", ", nodes.Keys)}.");

    /// <inheritdoc />
    public MeshNode Node(string name) => this[name].Node;

    /// <inheritdoc />
    public async Task<string> PairAsync(string showing, string entering, CancellationToken cancellationToken) =>
        await RunPairingAsync(showing, PairingMode.ShowUser, entering, PairingMode.JoinUser, PairingEntry.ByCode, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<string> PairByDiscoveryAsync(string showing, string entering, CancellationToken cancellationToken) =>
        await RunPairingAsync(showing, PairingMode.ShowUser, entering, PairingMode.JoinUser, PairingEntry.ByDiscovery, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<string> EnrolAsync(string user, string device, CancellationToken cancellationToken)
    {
        await HoldUserKeyAsync(user, cancellationToken).ConfigureAwait(false);
        string sas = await RunPairingAsync(device, PairingMode.ShowDevice, user, PairingMode.AddDevice, PairingEntry.ByCode, cancellationToken).ConfigureAwait(false);
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        await this[device].Node.WhenIdentityAsync(linked.Token).ConfigureAwait(false);
        return sas;
    }

    /// <inheritdoc />
    public async Task HoldUserKeyAsync(string name, CancellationToken cancellationToken)
    {
        RealNode node = this[name];
        await node.Keys.WriteAsync(KeyRole.User, node.Identity.UserPkcs8(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ConnectAsync(string a, string b, CancellationToken cancellationToken)
    {
        RealNode left = this[a];
        RealNode right = this[b];
        await UntilAsync(
            [left, right],
            () =>
            {
                if (MeshWaits.IsEstablishedWith(left.Node, right.Identity.DeviceId) && MeshWaits.IsEstablishedWith(right.Node, left.Identity.DeviceId))
                    return true;
                left.Discovery!.Announce();
                right.Discovery!.Announce();
                return false;
            },
            $"{a} and {b} established with each other, one session each",
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SettleAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        int agreeing = 0;
        try
        {
            while (true)
            {
                RealNode[] running = [.. nodes.Values.Where(n => n.IsRunning)];
                long[] seen = [.. running.Select(n => n.Node.Facts.LastSequence)];
                agreeing = MeshWaits.IsSettled([.. running.Select(n => n.Node)]) ? agreeing + 1 : 0;
                if (agreeing >= 3)
                    return;

                using var pause = new CancellationTokenSource(SettleRecheck);
                using var either = CancellationTokenSource.CreateLinkedTokenSource(pause.Token, linked.Token);
                try
                {
                    await Task.WhenAny(running.Select((n, i) => n.Node.Facts.WaitForAsync(_ => true, seen[i], either.Token))).Unwrap().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (pause.IsCancellationRequested && !linked.IsCancellationRequested)
                {
                    // Nothing recorded meanwhile; look again.
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Timeout("replication to settle: " + string.Join(" / ", nodes.Values.Where(n => n.IsRunning).Select(n => $"{n.Name}: {n.Node.Replication}")));
        }
    }

    /// <inheritdoc />
    public Task WaitUntilAsync(string name, Func<MeshNode, bool> condition, string what, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(condition);
        RealNode node = this[name];
        return UntilAsync([node], () => condition(node.Node), what, cancellationToken);
    }

    /// <inheritdoc />
    public Task<LocalClient> ConnectUserAsync(string name, string client, CancellationToken cancellationToken) =>
        this[name].ConnectAsync(new LocalClientOptions { Name = client }, cancellationToken);

    /// <inheritdoc />
    public Task<LocalClient> ConnectCliServiceAsync(string name, CancellationToken cancellationToken) =>
        this[name].ConnectAsync(new LocalClientOptions { Role = Role.Service, Name = TestCliService.Service.Name, Token = TestCliService.Token }, cancellationToken);

    /// <inheritdoc />
    public async Task<CliResult> CliAsync(string name, string commandLine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        RealNode node = this[name];
        var environment = new CliEnvironment
        {
            Version = "integration",
            HostAddress = node.Pipe.ToString(),
            ConnectHost = ct => LocalPipe.ConnectAsync(node.Pipe, ct),
            ConfigFile = $"the configuration of {name}",
            ReadConfig = () => $"{{ \"services\": {{ \"cli\": {{ \"external\": true, \"token\": \"{Hex(TestCliService.Token)}\" }} }} }}",
            CurrentDirectory = Path.GetTempPath(),
        };
        using var output = new StringWriter();
        using var error = new StringWriter();
        CliExitCode exit = await CliProgram.RunAsync(CliWords.Split(commandLine), output, error, environment, cancellationToken).ConfigureAwait(false);
        return new CliResult(exit, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
    }

    /// <inheritdoc />
    public Task<RunningService> RunServiceAsync(string name, IMeshService service, CancellationToken cancellationToken) => this[name].RunServiceAsync(service, this[name], cancellationToken);

    /// <inheritdoc />
    public Task<RunningService> RunServiceAsync(string name, IMeshService service, string stateOf, CancellationToken cancellationToken) =>
        this[name].RunServiceAsync(service, this[stateOf], cancellationToken);

    /// <inheritdoc />
    public async Task<ScriptedPeer> ConnectScriptedPeerAsync(string target, TestIdentity identity, CancellationToken cancellationToken)
    {
        Stream connection = await new TcpDialer(TimeProvider.System).DialAsync(this[target].Node.MeshEndpoint, cancellationToken).ConfigureAwait(false);
        return await ScriptedPeer.ConnectAsync(connection, identity, TimeProvider.System, ScriptedPeerOptions.Default, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopNodeAsync(string name, CancellationToken cancellationToken) => this[name].StopAsync(cancellationToken);

    /// <inheritdoc />
    public Task StartNodeAsync(string name, CancellationToken cancellationToken) => this[name].StartAsync(cancellationToken);

    /// <summary>Stops every node and deletes what they kept.</summary>
    /// <returns>A task that completes when everything is released.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (RealNode node in nodes.Values)
            await node.StopAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still held by a closing handle; the system's temporary folder is cleaned eventually.
        }
    }

    private static string Hex(ServiceToken token)
    {
        Span<byte> bytes = stackalloc byte[32];
        token.CopyTo(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Re-checks after every fact any of the nodes records, within the deadline.
    private async Task UntilAsync(RealNode[] watched, Func<bool> condition, string what, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            while (true)
            {
                long[] seen = [.. watched.Select(n => n.Node.Facts.LastSequence)];
                if (condition())
                    return;
                await Task.WhenAny(watched.Select((n, i) => n.Node.Facts.WaitForAsync(_ => true, seen[i], linked.Token))).Unwrap().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Timeout(what);
        }
    }

    private async Task<string> RunPairingAsync(string showing, PairingMode showMode, string entering, PairingMode enterMode, PairingEntry entry, CancellationToken cancellationToken)
    {
        RealNode shower = this[showing];
        RealNode enterer = this[entering];
        LocalClient show = await shower.ConnectAsync(new LocalClientOptions { Name = "fixture pairing" }, cancellationToken).ConfigureAwait(false);
        await using (show.ConfigureAwait(false))
        {
            LocalClient enter = await enterer.ConnectAsync(new LocalClientOptions { Name = "fixture pairing" }, cancellationToken).ConfigureAwait(false);
            await using (enter.ConfigureAwait(false))
                return await MeshPairing.RunAsync((showing, shower.Node, show), showMode, (entering, enterer.Node, enter), enterMode, entry, cancellationToken).ConfigureAwait(false);
        }
    }

    private TestMeshException Timeout(string what) => new(
        $"Waited {TestMesh.Deadline.TotalSeconds:0} s on sockets for {what}, in vain. "
            + string.Join(" ", nodes.Values.Where(n => n.IsRunning).Select(n => $"The last facts of {n.Name}: {string.Join(" | ", n.Node.Facts.Recent().TakeLast(16))}.")),
        "Read the facts: a HandshakeFailed or RefusedBeforeHello names what was refused, a BackedOff when the next dial is due.");
}
