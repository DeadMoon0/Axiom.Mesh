using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>GetTotal</c> (435, LOCAL-SPEC.md §10.3): Returns a series' lifetime total for a key, or a gauge's last value.</summary>
public sealed record GetTotal : LocalRequest, IRequest<GetTotal, TelemetryTotal>
{
    /// <summary>The message id, 435 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 435;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "GetTotal";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §10.3).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The series.</summary>
    public required string Series { get; init; }

    /// <summary>The key.</summary>
    public string Key { get; init; } = "";

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out TelemetryTotal ok)
    {
        bool read = TelemetryTotal.TryRead(ref reader, out TelemetryTotal? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, TelemetryTotal ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out GetTotal? value)
    {
        var read = new GetTotal
        {
            Series = Wire.ReadString(ref reader, FieldBounds.NameLength),
            Key = Wire.ReadString(ref reader, FieldBounds.TelemetryKeyLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Series, FieldBounds.NameLength);
        Wire.WriteString(ref writer, Key, FieldBounds.TelemetryKeyLength);
    }
}
