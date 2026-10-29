using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>QueryTelemetry</c> (434, LOCAL-SPEC.md §10.3): Returns a series' points for a key within a range of at most 7 days.</summary>
public sealed record QueryTelemetry : LocalRequest, IRequest<QueryTelemetry, IReadOnlyList<TelemetryPoint>>
{
    /// <summary>The message id, 434 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 434;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "QueryTelemetry";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §10.3).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The series.</summary>
    public required string Series { get; init; }

    /// <summary>The key.</summary>
    public string Key { get; init; } = "";

    /// <summary>Inclusive, rounded down to the minute.</summary>
    public ulong From { get; init; }

    /// <summary>Exclusive, rounded down to the minute.</summary>
    public ulong To { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out IReadOnlyList<TelemetryPoint> ok)
    {
        ok = LocalWire.ReadList<TelemetryPoint>(ref reader, FieldBounds.PointsCount);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, IReadOnlyList<TelemetryPoint> ok)
    {
        LocalWire.WriteList(ref writer, ok, FieldBounds.PointsCount);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out QueryTelemetry? value)
    {
        var read = new QueryTelemetry
        {
            Series = Wire.ReadString(ref reader, FieldBounds.NameLength),
            Key = Wire.ReadString(ref reader, FieldBounds.TelemetryKeyLength),
            From = reader.UInt64BigEndian(),
            To = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Series, FieldBounds.NameLength);
        Wire.WriteString(ref writer, Key, FieldBounds.TelemetryKeyLength);
        writer.UInt64BigEndian(From);
        writer.UInt64BigEndian(To);
    }
}
