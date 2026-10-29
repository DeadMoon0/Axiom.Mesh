using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>ServiceAccepted</c> (11, SPEC.md §7.10), Established only: the answer that switches the connection to the named
/// service protocol. A phase-1 node never sends <c>ServiceOpen</c>, so <see cref="Amp.Definition"/> treats a received
/// one as a message in the wrong state.
/// </summary>
public sealed class ServiceAccepted : MeshMessage, IWireMessage<ServiceAccepted>
{
    /// <summary>The acceptance of a service protocol.</summary>
    /// <param name="service">The service protocol name: at most 64 UTF-8 bytes.</param>
    /// <exception cref="ArgumentException">The name is too long, holds a NUL or an unpaired surrogate.</exception>
    public ServiceAccepted(string service)
    {
        Wire.StringLength(service, Limits.MaxServiceNameLength, nameof(service));
        Service = service;
    }

    /// <inheritdoc />
    public override ushort Id => MessageIds.ServiceAccepted;

    /// <summary>The service protocol name.</summary>
    public string Service { get; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out ServiceAccepted message)
    {
        string service = Wire.ReadString(ref reader, Limits.MaxServiceNameLength);
        message = new ServiceAccepted(reader.Ok ? service : string.Empty);
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => Wire.WriteString(ref writer, Service, Limits.MaxServiceNameLength);

    /// <inheritdoc />
    public override string ToString() => $"ServiceAccepted {Service}";
}
