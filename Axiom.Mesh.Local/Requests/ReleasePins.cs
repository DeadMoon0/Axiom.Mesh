using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ReleasePins</c> (357, LOCAL-SPEC.md §6.5): Removes every pin with a reason and source, to clean up after a service; the OK body counts them.</summary>
public sealed record ReleasePins : LocalRequest, IRequest<ReleasePins, uint>
{
    /// <summary>The message id, 357 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 357;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ReleasePins";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.5).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>User or Service.</summary>
    public required PinReason Reason { get; init; }

    /// <summary>The pins' source (§5.11); for Service, H(service name).</summary>
    public Hash32 Source { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out uint ok)
    {
        ok = reader.UInt32BigEndian();
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, uint ok)
    {
        writer.UInt32BigEndian(ok);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ReleasePins? value)
    {
        var read = new ReleasePins
        {
            Reason = (PinReason)reader.UInt8(),
            Source = Hash32.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt8((byte)Reason);
        Source.Write(ref writer);
    }
}
