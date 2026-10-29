using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>One minute of a telemetry series (LOCAL-SPEC.md §5.17).</summary>
public sealed record TelemetryPoint : IWireRecord<TelemetryPoint>
{
    /// <summary>The start of the minute, a multiple of 60 000.</summary>
    public ulong Minute { get; init; }

    /// <summary>Counter: the sum within the minute; gauge: the last value within it.</summary>
    public ulong Value { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out TelemetryPoint? value)
    {
        var read = new TelemetryPoint
        {
            Minute = reader.UInt64BigEndian(),
            Value = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        writer.UInt64BigEndian(Minute);
        writer.UInt64BigEndian(Value);
    }
}
