using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListKeys</c> (433, LOCAL-SPEC.md §10.3): Lists a series' keys, a page at a time.</summary>
public sealed record ListKeys : LocalRequest, IRequest<ListKeys, KeyPage>
{
    /// <summary>The message id, 433 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 433;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListKeys";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §10.3).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The series.</summary>
    public required string Series { get; init; }

    /// <summary>The cursor; <see langword="null"/> from the start.</summary>
    public string? After { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out KeyPage ok)
    {
        bool read = KeyPage.TryRead(ref reader, out KeyPage? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, KeyPage ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListKeys? value)
    {
        var read = new ListKeys
        {
            Series = Wire.ReadString(ref reader, FieldBounds.NameLength),
            After = reader.Boolean() ? Wire.ReadString(ref reader, FieldBounds.TelemetryKeyLength) : null,
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Series, FieldBounds.NameLength);
        if (After is { } presentAfter)
        {
            writer.Boolean(true);
            Wire.WriteString(ref writer, presentAfter, FieldBounds.TelemetryKeyLength);
        }
        else
        {
            writer.Boolean(false);
        }
    }
}
