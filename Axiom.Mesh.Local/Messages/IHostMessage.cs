namespace Axiom.Mesh.Local;

/// <summary>
/// A message the Host may send (LOCAL-SPEC.md §3.3): <see cref="HelloAck"/>, <see cref="Bye"/>, <see cref="Ping"/>,
/// <see cref="Pong"/>, <see cref="Drain"/>, a <see cref="Response"/>, an <see cref="EventFrame"/> and a
/// <see cref="Callback"/>, plus the frames a client receives with ids it does not know (<see cref="UnknownEvent"/>,
/// <see cref="UnknownCallback"/>). What <see cref="LocalProtocol.ClientSide"/> reads.
/// </summary>
public interface IHostMessage
{
    /// <summary>The frame's message id (§14.1).</summary>
    ushort MessageId { get; }
}
