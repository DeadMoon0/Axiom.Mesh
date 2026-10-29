namespace Axiom.Mesh.Core;

/// <summary>Where an object or a change came from.</summary>
internal enum OriginKind
{
    /// <summary>This node: a local client, a service, the node itself.</summary>
    Local = 0,

    /// <summary>A peer, by its device.</summary>
    Peer = 1,
}
