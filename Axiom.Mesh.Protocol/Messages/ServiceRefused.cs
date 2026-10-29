using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>ServiceRefused</c> (12, SPEC.md §7.10), Established only: the refusal of a <see cref="ServiceOpen"/>. A reason this
/// version does not define is read as <see cref="ServiceRefusal.Unspecified"/>.
/// </summary>
public sealed class ServiceRefused : MeshMessage, IWireMessage<ServiceRefused>
{
    /// <summary>The refusal of a service protocol.</summary>
    /// <param name="service">The service protocol name: at most 64 UTF-8 bytes.</param>
    /// <param name="reason">Why.</param>
    /// <exception cref="ArgumentException">The name is too long, holds a NUL or an unpaired surrogate.</exception>
    public ServiceRefused(string service, ServiceRefusal reason)
    {
        Wire.StringLength(service, Limits.MaxServiceNameLength, nameof(service));
        Service = service;
        Reason = Enum.IsDefined(reason) ? reason : ServiceRefusal.Unspecified;
    }

    /// <inheritdoc />
    public override ushort Id => MessageIds.ServiceRefused;

    /// <summary>The service protocol name.</summary>
    public string Service { get; }

    /// <summary>Why the service was refused.</summary>
    public ServiceRefusal Reason { get; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out ServiceRefused message)
    {
        string service = Wire.ReadString(ref reader, Limits.MaxServiceNameLength);
        var reason = (ServiceRefusal)reader.UInt8();
        message = new ServiceRefused(reader.Ok ? service : string.Empty, reason);
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Service, Limits.MaxServiceNameLength);
        writer.UInt8((byte)Reason);
    }

    /// <inheritdoc />
    public override string ToString() => $"ServiceRefused {Service}: {Reason}";
}
