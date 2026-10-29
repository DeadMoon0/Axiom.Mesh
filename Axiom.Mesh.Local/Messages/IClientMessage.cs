namespace Axiom.Mesh.Local;

/// <summary>
/// A message a client may send (LOCAL-SPEC.md §3.3): <see cref="Hello"/>, <see cref="Bye"/>, <see cref="Ping"/>,
/// <see cref="Pong"/>, a <see cref="RequestFrame"/> (or an <see cref="UnknownRequest"/>, a request id the Host may not
/// know) and, from a service, a <see cref="Reply"/>. What <see cref="LocalProtocol.HostSide"/> reads.
/// </summary>
public interface IClientMessage
{
    /// <summary>The frame's message id (§14.1).</summary>
    ushort MessageId { get; }
}
