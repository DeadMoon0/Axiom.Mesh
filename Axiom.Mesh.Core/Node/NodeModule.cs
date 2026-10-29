using System.Collections.Generic;
using Axiom.LWSE.Modules;
using Axiom.LWSE.Observation;
using Axiom.LWSE.Protocols;
using Axiom.Mesh.Local;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core;

/// <summary>
/// Everything a node adds to its LWSE server (DESIGN.md §10.2): AMP/1 served by the <see cref="PeerSessionHandler"/>,
/// the local protocol's port, the <see cref="MeshTls"/> transport, the observer that records the node's facts, and the
/// node's loops (dialler, discovery, trust, eviction) as services that start and stop with the server.
/// </summary>
internal sealed class NodeModule : LWSEModule
{
    private readonly ISessionHandler<MeshMessage, MeshMessage> mesh;
    private readonly ISessionHandler<IClientMessage, IHostMessage> local;
    private readonly IServerObserver observer;
    private readonly IReadOnlyList<IServerService> services;
    private readonly PeerDialler dialler;

    /// <summary>The module of one node.</summary>
    /// <param name="mesh">The AMP/1 handler.</param>
    /// <param name="local">The local port's handler.</param>
    /// <param name="observer">The observer that records facts.</param>
    /// <param name="services">The node's loops, in start order.</param>
    /// <param name="dialler">The dialler, which receives the server's adoption seam when the server is built.</param>
    public NodeModule(
        ISessionHandler<MeshMessage, MeshMessage> mesh,
        ISessionHandler<IClientMessage, IHostMessage> local,
        IServerObserver observer,
        IReadOnlyList<IServerService> services,
        PeerDialler dialler)
    {
        this.mesh = mesh;
        this.local = local;
        this.observer = observer;
        this.services = services;
        this.dialler = dialler;
    }

    /// <inheritdoc />
    public override string Name => "Axiom.Mesh";

    /// <inheritdoc />
    protected override void Register(ModuleRegistrar registrar)
    {
        registrar.Protocol(Ports.MeshProtocolKey, _ => mesh);
        registrar.Protocol(Ports.LocalProtocolKey, _ => local);
        registrar.Transport(MeshTls.Key, static (options, _) => new MeshTlsLayer(options));
        registrar.Observer(observer);
        foreach (IServerService service in services)
            registrar.Service(service);
    }

    /// <inheritdoc />
    protected override void Build(ModuleBuildContext context) => dialler.Use(context.Adoption);
}
