using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The OK body of <c>GetTotal</c> (LOCAL-SPEC.md §10.3).</summary>
public sealed record TelemetryTotal : IWireRecord<TelemetryTotal>
{
    /// <summary>Counter: the lifetime sum; gauge: the last value.</summary>
    public ulong Value { get; init; }

    /// <summary>Counter: when counting began; gauge: the minute of the last value.</summary>
    public ulong Since { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out TelemetryTotal? value)
    {
        var read = new TelemetryTotal
        {
            Value = reader.UInt64BigEndian(),
            Since = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        writer.UInt64BigEndian(Value);
        writer.UInt64BigEndian(Since);
    }
}
