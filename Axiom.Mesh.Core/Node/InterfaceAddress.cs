using System.Net;

namespace Axiom.Mesh.Core;

/// <summary>One unicast address of a network interface, with what the pairing code's choice needs to know of the interface.</summary>
/// <param name="Address">The address.</param>
/// <param name="IsUp">Whether the interface is up.</param>
/// <param name="HasGateway">Whether the interface has a default gateway: the network other devices are usually on.</param>
internal readonly record struct InterfaceAddress(IPAddress Address, bool IsUp, bool HasGateway);
