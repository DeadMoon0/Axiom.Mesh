using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListSeries</c> (432, LOCAL-SPEC.md §10.3): Lists the telemetry series, ascending by name.</summary>
public sealed record ListSeries : LocalRequest, IRequest<ListSeries, IReadOnlyList<TelemetrySeries>>
{
    private static readonly ListSeries Empty = new();

    /// <summary>The message id, 432 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 432;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListSeries";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §10.3).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out IReadOnlyList<TelemetrySeries> ok)
    {
        ok = LocalWire.ReadList<TelemetrySeries>(ref reader, FieldBounds.SeriesCount);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, IReadOnlyList<TelemetrySeries> ok)
    {
        LocalWire.WriteList(ref writer, ok, FieldBounds.SeriesCount);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListSeries? value)
    {
        value = Empty;
        return true;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
    }
}
