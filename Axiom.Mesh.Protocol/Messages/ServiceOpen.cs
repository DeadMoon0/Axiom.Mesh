using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>ServiceOpen</c> (10, SPEC.md §7.10), Established only: asks to switch the connection to a service protocol. Phase 1
/// offers no service, so <see cref="Amp.Definition"/> answers every one with <see cref="ServiceRefused"/>
/// (<see cref="ServiceRefusal.Unknown"/>) and never sends one.
/// </summary>
public sealed class ServiceOpen : MeshMessage, IWireMessage<ServiceOpen>
{
    /// <summary>A request to open a service protocol.</summary>
    /// <param name="service">The service protocol name (§14.4): at most 64 UTF-8 bytes.</param>
    /// <exception cref="ArgumentException">The name is too long, holds a NUL or an unpaired surrogate.</exception>
    public ServiceOpen(string service)
    {
        Wire.StringLength(service, Limits.MaxServiceNameLength, nameof(service));
        Service = service;
    }

    /// <inheritdoc />
    public override ushort Id => MessageIds.ServiceOpen;

    /// <summary>The service protocol name.</summary>
    public string Service { get; }

    /// <summary>The refusal of this request.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>A <see cref="ServiceRefused"/> naming the same service.</returns>
    public ServiceRefused Refuse(ServiceRefusal reason) => new(Service, reason);

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out ServiceOpen message)
    {
        string service = Wire.ReadString(ref reader, Limits.MaxServiceNameLength);
        message = new ServiceOpen(reader.Ok ? service : string.Empty);
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => Wire.WriteString(ref writer, Service, Limits.MaxServiceNameLength);

    /// <inheritdoc />
    public override string ToString() => $"ServiceOpen {Service}";
}
