<identity>
    <package>Axiom.Mesh.Testing</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Core.SKILL.md</requires>
</identity>

<objective>
    Teach an agent to test Axiom.Mesh nodes in memory: many full nodes on one fake clock, real mutual TLS and AMP/1 over in-memory streams, scripted peers, waits on facts.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Testing. TestKeys (the seven published PKCS#8 keys, Model-only so lower suites link the file); TestIdentity (Of(name), All, RosterOf(user) cached seq-1 Roster listing every published device of the user, RosterOf(user, seq, removed...) signed anew, Epoch); TestMesh (StartAsync(TestMeshOptions), this[name] -> TestNode, Clock (FakeTimeProvider), Network, ConnectAsync(a, b), WaitUntilAsync(name, condition, what), WaitForFactAsync(name, match, after, what), StopNodeAsync, StartNodeAsync, Partition(names), Heal, Deadline 15 s real time); TestMeshOptions { Nodes, Trust (TrustSeed.OwnUsersOnly, MutuallyKnown(names), And(node, knows)), Silent (nodes that dial but never announce), Seed }; TestNode { Name, Identity, Address 10.0.0.n:41810, Environment, Discovery, Node, Device, User, IsRunning }; TestNodeEnvironment (InMemoryStorageEnvironment, InMemoryObjectFiles, InMemoryKeyStore surviving restarts; OpenAsync and CloseAsync per start); InMemoryNetwork (Listen, Unlisten, Connect, Hold and Release of the discovery board, Partition and Heal cutting the links across); FakeDiscovery (per node: Announce, Withdraw, IsSilent; board replay first, then live; source Mdns); InMemoryDialer; InMemoryKeyStore; ScriptedPeer (ConnectAsync(node, identity, ScriptedPeerOptions { From, Alpn }), or AcceptAsync(mesh, identity, address) to be dialled; HandshakeCompleted, HandshakeFailure, NegotiatedAlpn, RemoteDevice; Hello(seq), SendAsync, SendRawAsync (a frame straight to TLS, no session, no heartbeat), ExpectAsync&lt;T&gt;, ExpectByeAsync, ExpectClosedAsync, ExpectClosedWithoutHelloAsync); TestMeshException. Planned: PairAsync and EnrolAsync (M10), Cli(...) and SettleAsync (M11).
</package_scope>

<architecture>
    Every node is a full MeshNode with InMemory = true: its mesh port is a number reached through LWSEServer.ConnectInMemory, its outbound and local ports are adopt-only, so admission, MeshTls, AMP/1, observers and drain run exactly as on sockets. Discovery is a CompositeDiscovery over a FakeDiscovery (cache with repeatAfter 0), as the Host wires it. StartAsync holds the discovery board, starts each node and adopts its own user's first Roster as identity, applies the TrustSeed with AcceptUserAsync, then releases the board so the nodes find and dial each other on their own. One FakeTimeProvider drives every server, session, database, backoff and retry.
</architecture>

<best_practices>
    Wait on facts: take Node.Facts.LastSequence, cause the step, then WaitForFactAsync or WaitUntilAsync; ConnectAsync re-announces both nodes after every fact. Advance the clock exactly to the moment awaited (a backoff's end from PeerRecord.BackoffUntil, 60 s for a heartbeat) and then wait; never one long Advance across a retry. Hand one Roster instance to every node that should hold it (signatures are not deterministic). Use Silent to control who dials; use ScriptedPeer.AcceptAsync to get a session the node itself dialled. Dispose the mesh (await using) so every node drains.
</best_practices>

<pitfalls>
    A long Clock.Advance fires a retry and then expires the handshake that retry started (the dial runs after Advance returns). A scripted peer always presents its certificate: on Windows an in-process TLS client given none presents one an earlier client used, so the certificate-less case is tested at the node's callback. A dial that ends while an announcement arrives skips it (state Dialling); ConnectAsync repeats announcements, a bare WaitUntilAsync does not. CompositeDiscovery takes one observer: never observe a node's discovery yourself. Thread.Sleep and Task.Delay are refused by a convention test.
</pitfalls>
