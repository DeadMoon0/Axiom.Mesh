namespace Axiom.Mesh.Core;

/// <summary>What a request handler reaches: the connection the request came on, the local port and the node.</summary>
/// <param name="Connection">The connection: its role, process identity, subscriptions and operations.</param>
/// <param name="Port">The local port: its configuration and its connections.</param>
/// <param name="Node">The node: store, trust, peers, keys.</param>
internal sealed record RequestContext(LocalConnection Connection, LocalPort Port, MeshNode Node);
