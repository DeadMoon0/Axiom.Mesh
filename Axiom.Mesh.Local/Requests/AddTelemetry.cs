using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>AddTelemetry</c> (517, LOCAL-SPEC.md §9.9): Adds to one of this service's counters.</summary>
public sealed record AddTelemetry : LocalRequest, IRequest<AddTelemetry, NoContent>
{
    /// <summary>The message id, 517 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 517;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "AddTelemetry";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §9.9).</summary>
    public static Roles AllowedRoles => Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary><c>svc.&lt;service&gt;.&lt;name&gt;</c>.</summary>
    public required string Series { get; init; }

    /// <summary>The key.</summary>
    public string Key { get; init; } = "";

    /// <summary>What to add.</summary>
    public ulong Delta { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out NoContent ok)
    {
        ok = default;
        return true;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, NoContent ok)
    {
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out AddTelemetry? value)
    {
        var read = new AddTelemetry
        {
            Series = Wire.ReadString(ref reader, FieldBounds.NameLength),
            Key = Wire.ReadString(ref reader, FieldBounds.TelemetryKeyLength),
            Delta = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Series, FieldBounds.NameLength);
        Wire.WriteString(ref writer, Key, FieldBounds.TelemetryKeyLength);
        writer.UInt64BigEndian(Delta);
    }
}
