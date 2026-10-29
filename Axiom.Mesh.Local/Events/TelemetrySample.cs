using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>TelemetrySample</c> (1030, LOCAL-SPEC.md §7.3): A minute of a telemetry series closed, or (client capability bit 0) a partial sample of the current one.</summary>
public sealed record TelemetrySample : LocalEvent, IEvent<TelemetrySample>
{
    /// <summary>The message id, 1030 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1030;

    /// <summary>The topic whose subscriptions receive it (LOCAL-SPEC.md §7.2).</summary>
    public static Topic Topic => Topic.Telemetry;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The series.</summary>
    public string Series { get; init; } = "";

    /// <summary>The key.</summary>
    public string Key { get; init; } = "";

    /// <summary>The start of the minute.</summary>
    public ulong Minute { get; init; }

    /// <summary>The value.</summary>
    public ulong Value { get; init; }

    /// <summary>Whether the minute has not closed yet.</summary>
    public bool Partial { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out TelemetrySample? value)
    {
        var read = new TelemetrySample
        {
            Series = Wire.ReadString(ref reader, FieldBounds.NameLength),
            Key = Wire.ReadString(ref reader, FieldBounds.TelemetryKeyLength),
            Minute = reader.UInt64BigEndian(),
            Value = reader.UInt64BigEndian(),
            Partial = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Series, FieldBounds.NameLength);
        Wire.WriteString(ref writer, Key, FieldBounds.TelemetryKeyLength);
        writer.UInt64BigEndian(Minute);
        writer.UInt64BigEndian(Value);
        writer.Boolean(Partial);
    }
}
