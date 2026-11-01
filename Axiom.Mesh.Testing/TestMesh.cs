using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Discovery;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;
using Microsoft.Extensions.Time.Testing;

namespace Axiom.Mesh.Testing;

/// <summary>
/// An in-memory mesh of published test devices on one fake clock: every node a full <see cref="MeshNode"/> with its own
/// in-memory store, connected over an <see cref="InMemoryNetwork"/> through real mutual TLS and AMP/1. Nodes find each
/// other through <see cref="FakeDiscovery"/> and dial as on a LAN; a test seeds trust, connects, stops, restarts and
/// partitions them, and waits for what it expects through the nodes' facts, never by sleeping.
/// </summary>
/// <remarks>
/// Node <c>n</c> (counted from 1 in the order given) listens at <c>10.0.0.n:41810</c>. Every wait gives up after 15
/// seconds of real time with a <see cref="TestMeshException"/>; time on the fake clock only passes when the test
/// advances <see cref="Clock"/>.
/// </remarks>
public sealed class TestMesh : IMeshFixture
{
    /// <summary>How long a wait lasts in real time before it fails the test.</summary>
    public static TimeSpan Deadline { get; } = TimeSpan.FromSeconds(15);

    private readonly Dictionary<string, TestNode> nodes;
    private readonly MeshNodeOptions nodeOptions;

    private TestMesh(FakeTimeProvider clock, InMemoryNetwork network, Dictionary<string, TestNode> nodes, MeshNodeOptions nodeOptions)
    {
        Clock = clock;
        Network = network;
        this.nodes = nodes;
        this.nodeOptions = nodeOptions;
    }

    /// <summary>The mesh's one clock: every node's server, sessions, database and backoffs run on it.</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>The network the nodes are on.</summary>
    public InMemoryNetwork Network { get; }

    /// <summary>The nodes, in the order they were named.</summary>
    public IReadOnlyList<TestNode> Nodes => [.. nodes.Values];

    /// <summary>A node by name.</summary>
    /// <param name="name">Its published device name.</param>
    /// <returns>The node.</returns>
    /// <exception cref="KeyNotFoundException">The mesh has no such node.</exception>
    public TestNode this[string name] =>
        nodes.TryGetValue(name, out TestNode? node) ? node
        : throw new KeyNotFoundException($"The mesh has no node '{name}'; it has {string.Join(", ", nodes.Keys)}.");

    /// <summary>
    /// Starts the mesh: every node with its own user's Roster as its identity, then the trust of
    /// <see cref="TestMeshOptions.Trust"/>, and only then the discovery board, so that every dial meets a node that
    /// already knows whom it trusts.
    /// </summary>
    /// <param name="options">The nodes, their trust and the seed.</param>
    /// <param name="cancellationToken">Cancels starting.</param>
    /// <returns>The running mesh.</returns>
    public static async Task<TestMesh> StartAsync(TestMeshOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var clock = new FakeTimeProvider(TestIdentity.Epoch);
        var network = new InMemoryNetwork();
        var nodes = new Dictionary<string, TestNode>(StringComparer.Ordinal);
        var packages = new PackageBook(options.Seed);
        for (int i = 0; i < options.Nodes.Count; i++)
        {
            TestIdentity identity = TestIdentity.Of(options.Nodes[i]);
            if (nodes.ContainsKey(identity.Name))
                throw new ArgumentException($"'{identity.Name}' is named twice; a mesh holds each published device once.", nameof(options));
            var address = new IPEndPoint(new IPAddress([10, 0, 0, (byte)(i + 1)]), MeshNodeOptions.DefaultMeshPort);
            var discovery = new FakeDiscovery(network, new Announcement(identity.DeviceId, identity.UserId, address, DiscoverySource.Mdns))
            {
                IsSilent = options.Silent.Contains(identity.Name),
            };
            nodes[identity.Name] = new TestNode(identity, address, new TestNodeEnvironment(identity, clock, options.Seed + (ulong)i)
            {
                Loggers = options.Loggers,
                Addresses = [address.Address],
                Files = options.StoreDirectories.TryGetValue(identity.Name, out string? directory)
                    ? new DirectoryObjectFiles(directory, new DeterministicEntropy(options.Seed + (ulong)i))
                    : new InMemoryObjectFiles(),
            }, discovery, new InMemoryDialer(network, address.Address), packages, options.Local);
        }

        var nodeOptions = MeshNodeOptions.Default with { Local = options.Local with { HostUser = TestProcess.ServedUser } };
        var mesh = new TestMesh(clock, network, nodes, nodeOptions);
        network.Hold();
        try
        {
            foreach (TestNode node in nodes.Values)
            {
                await mesh.RunAsync(node, cancellationToken).ConfigureAwait(false);
                if (!options.WithoutIdentity.Contains(node.Name))
                    await node.Node.AdoptIdentityAsync(TestIdentity.RosterOf(node.Identity.UserName), node.Identity.UserName, cancellationToken).ConfigureAwait(false);
            }

            foreach ((string name, string knows) in options.Trust.Pairs)
            {
                TestNode other = mesh[knows];
                if (other.Identity.UserName != mesh[name].Identity.UserName && !options.WithoutIdentity.Contains(name))
                    await mesh[name].Node.AcceptUserAsync(TestIdentity.RosterOf(other.Identity.UserName), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await mesh.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        foreach ((string a, string b) in options.Severed)
            mesh.Sever(a, b);
        network.Release();
        return mesh;
    }

    /// <summary>
    /// Waits until two nodes hold an established session to each other, one each (the duplicate rule has settled), and no
    /// other connection between them is still opening: every dial either one started toward the other has reached it, and
    /// every connection either one accepted from the other is that session or closed. Both are announced again at first
    /// and after every fact either records, as discovery would repeat them; a backoff still holds a dial back until the
    /// clock reaches its end.
    /// </summary>
    /// <remarks>
    /// When both dial at once, the second connection may still be in its handshake while the first is established; a
    /// test that then advances the clock would expire that handshake (10 seconds) or its first <c>Hello</c> and see a
    /// session end it did not cause. So this returns only once the second connection has closed.
    /// </remarks>
    /// <param name="a">One node.</param>
    /// <param name="b">The other.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when both are established.</returns>
    /// <exception cref="TestMeshException">They did not connect within the deadline.</exception>
    public async Task ConnectAsync(string a, string b, CancellationToken cancellationToken)
    {
        TestNode left = this[a];
        TestNode right = this[b];
        using var deadline = new CancellationTokenSource(Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            while (true)
            {
                long leftSeen = left.Node.Facts.LastSequence;
                long rightSeen = right.Node.Facts.LastSequence;
                if (IsEstablishedWith(left.Node, right.Device) && IsEstablishedWith(right.Node, left.Device)
                    && NothingOpening(left, right) && NothingOpening(right, left))
                    return;

                // Announce again after every change: an announcement heard while a dial was still ending is passed over.
                left.Discovery.Announce();
                right.Discovery.Announce();
                await Task.WhenAny(
                    left.Node.Facts.WaitForAsync(_ => true, leftSeen, linked.Token),
                    right.Node.Facts.WaitForAsync(_ => true, rightSeen, linked.Token)).Unwrap().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Timeout(a, $"{a} and {b} established with each other, one session each");
        }
    }

    /// <summary>Waits until a condition on a node's state holds, re-checking after every fact the node records.</summary>
    /// <param name="name">The node.</param>
    /// <param name="condition">What must hold.</param>
    /// <param name="what">What is awaited, for the failure message.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    /// <exception cref="TestMeshException">The condition did not hold within the deadline.</exception>
    public async Task WaitUntilAsync(string name, Func<MeshNode, bool> condition, string what, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(condition);
        MeshNode node = this[name].Node;
        using var deadline = new CancellationTokenSource(Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            while (true)
            {
                long seen = node.Facts.LastSequence;
                if (condition(node))
                    return;
                await node.Facts.WaitForAsync(_ => true, seen, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Timeout(name, what);
        }
    }

    /// <summary>Waits for a fact a node records after <paramref name="after"/>.</summary>
    /// <param name="name">The node.</param>
    /// <param name="match">Which fact.</param>
    /// <param name="after">Only facts after this sequence number count; take <see cref="NodeFacts.LastSequence"/> before causing it.</param>
    /// <param name="what">What is awaited, for the failure message.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The fact.</returns>
    /// <exception cref="TestMeshException">No such fact within the deadline.</exception>
    public async Task<NodeFact> WaitForFactAsync(string name, Func<NodeFact, bool> match, long after, string what, CancellationToken cancellationToken)
    {
        MeshNode node = this[name].Node;
        using var deadline = new CancellationTokenSource(Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            return await node.Facts.WaitForAsync(match, after, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Timeout(name, what);
        }
    }

    /// <summary>
    /// Creates a node's identity over its local port, as the command line does (LOCAL-SPEC.md §6.1): the node creates the
    /// user key, a Roster with seq 1 holding its device, and swaps its device credential in. For a node of
    /// <see cref="TestMeshOptions.WithoutIdentity"/>.
    /// </summary>
    /// <param name="name">The node.</param>
    /// <param name="user">The user's display name.</param>
    /// <param name="device">The device's name in the Roster.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The identity the node answered with.</returns>
    /// <exception cref="TestMeshException">The node refused the request.</exception>
    public async Task<Identity> CreateIdentityAsync(string name, string user, string device, CancellationToken cancellationToken)
    {
        TestNode node = this[name];
        LocalClient client = await node.ConnectUserAsync("test-mesh", cancellationToken).ConfigureAwait(false);
        await using (client.ConfigureAwait(false))
        {
            LocalResult<Identity> created = await client.SendAsync(new CreateIdentity { UserName = user, DeviceName = device }, cancellationToken).ConfigureAwait(false);
            if (!created.IsOk)
            {
                throw new TestMeshException(
                    $"{name} refused CreateIdentity with {created.Status}: {created.Error?.Message}",
                    "Start the node without an identity (TestMeshOptions.WithoutIdentity), and create it once.");
            }

            return created.Value;
        }
    }

    /// <summary>
    /// Pairs the users of two nodes over the local port, as two people at their screens would (PAIR-SPEC.md, LOCAL-SPEC.md
    /// §6.2): <paramref name="showing"/> starts <c>ShowUser</c>, <paramref name="entering"/> enters its code with
    /// <c>JoinUser</c>, both wait until the short authentication strings show, compare them, and confirm. Waits on the
    /// nodes' facts, never by time; the dial that follows under AMP/1 is not awaited (see <see cref="ConnectAsync"/>).
    /// </summary>
    /// <param name="showing">The node that shows the code.</param>
    /// <param name="entering">The node that enters it.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The short authentication string both sides showed.</returns>
    /// <exception cref="TestMeshException">A side refused a step, the strings differ, or a step did not happen within the deadline.</exception>
    public Task<string> PairAsync(string showing, string entering, CancellationToken cancellationToken) =>
        RunPairingAsync(showing, PairingMode.ShowUser, entering, PairingMode.JoinUser, cancellationToken);

    /// <summary>
    /// Enrols a new device into the user of another node (PAIR-SPEC.md §9.2, §9.3): <paramref name="device"/>, which has no
    /// identity, starts <c>ShowDevice</c>, <paramref name="user"/> enters its code with <c>AddDevice</c> (its user key is
    /// placed in its key store first), both compare and confirm, and the device holds the user's identity once this
    /// returns.
    /// </summary>
    /// <param name="user">A node of the user, which signs the new Roster.</param>
    /// <param name="device">The node being enrolled; started without an identity (<see cref="TestMeshOptions.WithoutIdentity"/>).</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The short authentication string both sides showed.</returns>
    /// <exception cref="TestMeshException">A side refused a step, the strings differ, or a step did not happen within the deadline.</exception>
    public async Task<string> EnrolAsync(string user, string device, CancellationToken cancellationToken)
    {
        await this[user].HoldUserKeyAsync(cancellationToken).ConfigureAwait(false);
        string sas = await RunPairingAsync(device, PairingMode.ShowDevice, user, PairingMode.AddDevice, cancellationToken).ConfigureAwait(false);
        using var deadline = new CancellationTokenSource(Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            await this[device].Node.WhenIdentityAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Timeout(device, $"{device} to hold the identity of {this[user].Identity.UserName}");
        }

        return sas;
    }

    /// <summary>Stops a node as a shutdown would: its sessions say <c>Bye(Shutdown)</c>, its announcement leaves the board.</summary>
    /// <param name="name">The node.</param>
    /// <param name="cancellationToken">Cuts the drain short.</param>
    /// <returns>A task that completes when it stopped.</returns>
    public async Task StopNodeAsync(string name, CancellationToken cancellationToken)
    {
        TestNode node = this[name];
        if (!node.IsRunning)
            return;
        Network.Unlisten(node.Address);
        node.Discovery.Withdraw();
        await node.CloseClientsAsync().ConfigureAwait(false);
        await node.Node.StopAsync(cancellationToken).ConfigureAwait(false);
        node.Running(null);
        await node.Environment.CloseAsync().ConfigureAwait(false);
    }

    /// <summary>Starts a stopped node again on what it kept: its store, its trust, its keys.</summary>
    /// <param name="name">The node.</param>
    /// <param name="cancellationToken">Cancels starting.</param>
    /// <returns>A task that completes when it runs.</returns>
    public Task StartNodeAsync(string name, CancellationToken cancellationToken) => RunAsync(this[name], cancellationToken);

    /// <summary>Splits the network: the named nodes on one side, the others on the other; connections across are cut.</summary>
    /// <param name="names">The nodes of one side.</param>
    public void Partition(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        Network.Partition(names.Select(n => this[n].Address.Address));
    }

    /// <summary>Joins the network again and mends every severed link; the nodes hear each other again and redial.</summary>
    public void Heal() => Network.Heal();

    /// <summary>
    /// Cuts the link between two nodes alone: their session ends and they never dial each other, while each still reaches
    /// every other node. A third node between them then relays (SPEC.md Appendix B).
    /// </summary>
    /// <param name="a">One node.</param>
    /// <param name="b">The other.</param>
    public void Sever(string a, string b) => Network.Sever(this[a].Address.Address, this[b].Address.Address);

    /// <summary>
    /// Waits until replication has nothing left to do on any running node: no command for a store, no change unhandled,
    /// no want asked or askable, every session's lanes empty, and every message one node sent handled by its peer. A want
    /// that waits for a source no node offers does not hold it up. Read on the nodes' <see cref="MeshNode.Replication"/>,
    /// re-checked after every fact and every few milliseconds; settled means three checks in a row agree.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when the mesh is settled.</returns>
    /// <exception cref="TestMeshException">It did not settle within the deadline.</exception>
    public async Task SettleAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        int agreeing = 0;
        try
        {
            while (true)
            {
                TestNode[] running = [.. nodes.Values.Where(n => n.IsRunning)];
                long[] seen = [.. running.Select(n => n.Node.Facts.LastSequence)];
                if (IsSettled(running))
                {
                    if (++agreeing >= 3)
                        return;
                }
                else
                {
                    agreeing = 0;
                }

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
            throw Timeout(nodes.Keys.First(), "replication to settle: " + string.Join(" / ", nodes.Values.Where(n => n.IsRunning).Select(n => $"{n.Name}: {n.Node.Replication}")));
        }
    }

    /// <inheritdoc />
    TimeProvider IMeshFixture.Clock => Clock;

    /// <inheritdoc />
    MeshNode IMeshFixture.Node(string name) => this[name].Node;

    /// <inheritdoc />
    Task IMeshFixture.HoldUserKeyAsync(string name, CancellationToken cancellationToken) => this[name].HoldUserKeyAsync(cancellationToken);

    /// <inheritdoc />
    Task<LocalClient> IMeshFixture.ConnectUserAsync(string name, string client, CancellationToken cancellationToken) => this[name].ConnectUserAsync(client, cancellationToken);

    /// <inheritdoc />
    Task<LocalClient> IMeshFixture.ConnectCliServiceAsync(string name, CancellationToken cancellationToken) =>
        this[name].ConnectServiceAsync(TestCliService.Service.Name, TestCliService.Token, cancellationToken);

    /// <inheritdoc />
    Task<CliResult> IMeshFixture.CliAsync(string name, string commandLine, CancellationToken cancellationToken) => this[name].CliAsync(commandLine, cancellationToken);

    /// <inheritdoc />
    Task<RunningService> IMeshFixture.RunServiceAsync(string name, IMeshService service, CancellationToken cancellationToken) => this[name].RunServiceAsync(service, cancellationToken);

    /// <inheritdoc />
    Task<ScriptedPeer> IMeshFixture.ConnectScriptedPeerAsync(string target, TestIdentity identity, CancellationToken cancellationToken) =>
        ScriptedPeer.ConnectAsync(this[target], identity, ScriptedPeerOptions.Default, cancellationToken);

    /// <summary>Stops every node.</summary>
    /// <returns>A task that completes when all stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (TestNode node in nodes.Values)
            await StopNodeAsync(node.Name, CancellationToken.None).ConfigureAwait(false);
    }

    private static TimeSpan SettleRecheck { get; } = TimeSpan.FromMilliseconds(5);

    private static bool IsEstablishedWith(MeshNode node, DeviceId peer) => MeshWaits.IsEstablishedWith(node, peer);

    private static bool IsSettled(TestNode[] running) => MeshWaits.IsSettled([.. running.Select(n => n.Node)]);

    // Read from the node's facts: one connection with the other node's address is open (accepted or dialled, not closed),
    // and every dial to the other's mesh endpoint was followed by its connection or its failure. A fact the ring dropped
    // counts neither way, so a long test never waits on a lost one.
    private static bool NothingOpening(TestNode node, TestNode other)
    {
        var open = new HashSet<long>();
        int dials = 0;
        int reached = 0;
        foreach (NodeFact fact in node.Node.Facts.Recent())
        {
            if (fact.Remote is not { } remote || !remote.Address.Equals(other.Address.Address))
                continue;
            bool toEndpoint = remote.Equals(other.Address);
            switch (fact.Kind)
            {
                case NodeFactKind.Accepted:
                    open.Add(fact.Connection);
                    reached += toEndpoint ? 1 : 0;
                    break;
                case NodeFactKind.Closed or NodeFactKind.Rejected:
                    open.Remove(fact.Connection);
                    break;
                case NodeFactKind.Dialling:
                    dials += toEndpoint ? 1 : 0;
                    break;
                case NodeFactKind.DialFailed:
                    reached += toEndpoint ? 1 : 0;
                    break;
            }
        }

        return open.Count <= 1 && reached >= dials;
    }

    private TestMeshException Timeout(string name, string what) => new(
        $"Waited {Deadline.TotalSeconds:0} s for {what}, in vain. "
            + string.Join(" ", nodes.Values.Where(n => n.IsRunning).Select(n => $"The last facts of {n.Name}: {string.Join(" | ", n.Node.Facts.Recent().TakeLast(16))}.")),
        $"Read the facts of {name} first: a HandshakeFailed or RefusedBeforeHello names what was refused, a BackedOff when the next dial is due.",
        "Advance the mesh's Clock when the awaited step waits for time to pass (a backoff, a heartbeat), in steps no longer than the step needs: a dial a timer starts runs after Advance returns, and a long Advance expires its handshake.");


    // Both sides of one pairing over their local ports: show, enter, compare, confirm (the showing side first), Paired.
    private async Task<string> RunPairingAsync(string showing, PairingMode showMode, string entering, PairingMode enterMode, CancellationToken cancellationToken)
    {
        TestNode shower = this[showing];
        TestNode enterer = this[entering];
        LocalClient show = await shower.ConnectUserAsync("test-mesh pairing", cancellationToken).ConfigureAwait(false);
        await using (show.ConfigureAwait(false))
        {
            LocalClient enter = await enterer.ConnectUserAsync("test-mesh pairing", cancellationToken).ConfigureAwait(false);
            await using (enter.ConfigureAwait(false))
                return await MeshPairing.RunAsync((showing, shower.Node, show), showMode, (entering, enterer.Node, enter), enterMode, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits until a node's current pairing (<c>GetPairing</c>, LOCAL-SPEC.md §6.2) satisfies a condition, reading it again
    /// after every <see cref="NodeFactKind.PairingChanged"/> fact.
    /// </summary>
    /// <param name="name">The node.</param>
    /// <param name="client">A user client of that node.</param>
    /// <param name="condition">What must hold.</param>
    /// <param name="what">What is awaited, for the failure message.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The pairing once it holds.</returns>
    /// <exception cref="TestMeshException">It did not hold within the deadline.</exception>
    public Task<Pairing> WaitForPairingAsync(string name, LocalClient client, Func<Pairing, bool> condition, string what, CancellationToken cancellationToken) =>
        MeshPairing.WaitForAsync((name, this[name].Node, client), condition, what, cancellationToken);

    private async Task RunAsync(TestNode node, CancellationToken cancellationToken)
    {
        if (node.IsRunning)
            return;
        MeshNodeEnvironment environment = await node.Environment.OpenAsync(Composite(node), node.Dialer, cancellationToken).ConfigureAwait(false);
        MeshNode running;
        try
        {
            running = await MeshNode.StartAsync(nodeOptions with { DeviceName = node.Name }, environment, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await node.Environment.CloseAsync().ConfigureAwait(false);
            throw;
        }

        node.Running(running);
        Network.Listen(node.Address, running.ConnectInMemory);
    }

    // As the Host does: discovery through a CompositeDiscovery, whose cache the node consults after a trust change. Every
    // claim passes the cache, so a test may announce again at any time.
    private IDiscovery Composite(TestNode node) => new CompositeDiscovery(new CompositeDiscoveryOptions
    {
        Parts = [node.Discovery],
        Time = Clock,
        Cache = new AnnouncementCache(256, TimeSpan.Zero),
    });
}
