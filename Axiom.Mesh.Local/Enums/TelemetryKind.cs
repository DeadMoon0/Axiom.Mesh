namespace Axiom.Mesh.Local;

/// <summary>Whether a telemetry series counts or samples (LOCAL-SPEC.md §5.17, §10.1).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum TelemetryKind : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>A counter: the sum within each minute and a lifetime total.</summary>
    Counter = 1,

    /// <summary>A gauge: the last value within each minute.</summary>
    Gauge = 2,
}
