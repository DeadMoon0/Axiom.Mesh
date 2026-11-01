using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Testing;

/// <summary>
/// A running mesh of published test devices (<see cref="TestIdentity"/>) as a scenario sees it, whatever carries it: in
/// memory on one fake clock (<see cref="TestMesh"/>), or real nodes on loopback sockets with real TLS, a store and an index
/// on disk, and the real pipe (the integration suite's fixture). A scenario written against this interface runs unchanged
/// on both, and so proves the same end state twice.
/// </summary>
/// <remarks>
/// Nodes are named by their published device name (<c>alice-laptop</c>, <c>alice-phone</c>, <c>bob-desktop</c>,
/// <c>eve-device</c>); each starts as a device of its own user, knowing no other user, unless it started without an
/// identity. Users come to know each other only by pairing (<see cref="PairAsync"/>). The local port of every node lists
/// the <c>cli</c> service of <see cref="TestCliService"/>. Every wait gives up after <see cref="TestMesh.Deadline"/> of real
/// time with a <see cref="TestMeshException"/>; nothing here sleeps.
/// </remarks>
public interface IMeshFixture : IAsyncDisposable
{
    /// <summary>The mesh's clock: fake in memory (it moves only when a test advances it), the system's on sockets.</summary>
    TimeProvider Clock { get; }

    /// <summary>A running node, for its facts, peers and replication status.</summary>
    /// <param name="name">The node's published device name.</param>
    /// <returns>The node.</returns>
    /// <exception cref="InvalidOperationException">The node is stopped.</exception>
    MeshNode Node(string name);

    /// <summary>
    /// Pairs the users of two nodes over their local ports (PAIR-SPEC.md, LOCAL-SPEC.md §6.2): one shows a code, the other
    /// enters it, both compare the short authentication string and confirm.
    /// </summary>
    /// <param name="showing">The node that shows the code.</param>
    /// <param name="entering">The node that enters it.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The short authentication string both sides showed.</returns>
    Task<string> PairAsync(string showing, string entering, CancellationToken cancellationToken);

    /// <summary>Enrols a node started without an identity as a new device of another node's user (PAIR-SPEC.md §9.2, §9.3).</summary>
    /// <param name="user">A node of the user; it signs the new Roster.</param>
    /// <param name="device">The node being enrolled.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The short authentication string both sides showed.</returns>
    Task<string> EnrolAsync(string user, string device, CancellationToken cancellationToken);

    /// <summary>Puts the user key into a node's key store, so that its user-key requests (LOCAL-SPEC.md §2.2) succeed there.</summary>
    /// <param name="name">The node.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the key is stored.</returns>
    Task HoldUserKeyAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Waits until two nodes hold one established session to each other each and no other connection between them is
    /// still opening; makes each findable by the other first, as discovery would.
    /// </summary>
    /// <param name="a">One node.</param>
    /// <param name="b">The other.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when both are established.</returns>
    Task ConnectAsync(string a, string b, CancellationToken cancellationToken);

    /// <summary>Waits until replication has nothing left to do on any running node and every message sent was handled.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when the mesh is settled.</returns>
    Task SettleAsync(CancellationToken cancellationToken);

    /// <summary>Waits until a condition on a node holds, re-checking after every fact the node records.</summary>
    /// <param name="name">The node.</param>
    /// <param name="condition">What must hold.</param>
    /// <param name="what">What is awaited, for the failure message.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    Task WaitUntilAsync(string name, Func<MeshNode, bool> condition, string what, CancellationToken cancellationToken);

    /// <summary>Connects a user client to a node's local port, as a program of the served user would, and says <c>Hello</c>.</summary>
    /// <param name="name">The node.</param>
    /// <param name="client">The client's name.</param>
    /// <param name="cancellationToken">Cancels the opening.</param>
    /// <returns>The client, Ready; the fixture closes it when the node stops.</returns>
    Task<LocalClient> ConnectUserAsync(string name, string client, CancellationToken cancellationToken);

    /// <summary>
    /// Connects a client in the service role as the external service <c>cli</c> of <see cref="TestCliService"/>, presenting
    /// its token (LOCAL-SPEC.md §4.2): it may register kinds, write Entries and put objects and files.
    /// </summary>
    /// <param name="name">The node.</param>
    /// <param name="cancellationToken">Cancels the opening.</param>
    /// <returns>The client, Ready; the fixture closes it when the node stops.</returns>
    Task<LocalClient> ConnectCliServiceAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a service in process beside a node, connected to its local port as the external service the node's configuration
    /// lists for it (<see cref="TestServices.With"/>), its state kept across runs: in memory on one fake clock, or in a
    /// directory of the node over the real pipe. Returns once the service is connected; dispose the result to stop it.
    /// </summary>
    /// <param name="name">The node.</param>
    /// <param name="service">The service.</param>
    /// <param name="cancellationToken">Stops waiting for the connection.</param>
    /// <returns>The running service.</returns>
    Task<RunningService> RunServiceAsync(string name, IMeshService service, CancellationToken cancellationToken);

    /// <summary>Runs one <c>axiom-mesh</c> command line against a node, as the command line runs against its Host.</summary>
    /// <param name="name">The node.</param>
    /// <param name="commandLine">The arguments after <c>axiom-mesh</c>, quoted as a shell would.</param>
    /// <param name="cancellationToken">Ends a command that runs until cancelled.</param>
    /// <returns>The exit code and both outputs.</returns>
    Task<CliResult> CliAsync(string name, string commandLine, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a connection to a node's mesh listener and runs the TLS handshake with a published identity, for a scenario
    /// that sends what a real node never would (<see cref="ScriptedPeer"/>).
    /// </summary>
    /// <param name="target">The node.</param>
    /// <param name="identity">The device the peer presents.</param>
    /// <param name="cancellationToken">Cancels connecting.</param>
    /// <returns>The peer, its handshake done or failed.</returns>
    Task<ScriptedPeer> ConnectScriptedPeerAsync(string target, TestIdentity identity, CancellationToken cancellationToken);

    /// <summary>Stops a node as a shutdown would; it keeps its store, index and keys.</summary>
    /// <param name="name">The node.</param>
    /// <param name="cancellationToken">Cuts the drain short.</param>
    /// <returns>A task that completes when it stopped.</returns>
    Task StopNodeAsync(string name, CancellationToken cancellationToken);

    /// <summary>Starts a stopped node again on what it kept.</summary>
    /// <param name="name">The node.</param>
    /// <param name="cancellationToken">Cancels starting.</param>
    /// <returns>A task that completes when it runs.</returns>
    Task StartNodeAsync(string name, CancellationToken cancellationToken);
}
