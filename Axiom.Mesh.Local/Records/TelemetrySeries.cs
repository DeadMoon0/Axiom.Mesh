using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A telemetry series (LOCAL-SPEC.md §5.17, §10.1).</summary>
public sealed record TelemetrySeries : IWireRecord<TelemetrySeries>
{
    /// <summary>The series (§14.13).</summary>
    public string Name { get; init; } = "";

    /// <summary>Counter or gauge.</summary>
    public TelemetryKind Kind { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out TelemetrySeries? value)
    {
        var read = new TelemetrySeries
        {
            Name = Wire.ReadString(ref reader, FieldBounds.NameLength),
            Kind = (TelemetryKind)reader.UInt8(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Name, FieldBounds.NameLength);
        writer.UInt8((byte)Kind);
    }
}
