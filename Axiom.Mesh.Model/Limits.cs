namespace Axiom.Mesh;

/// <summary>
/// Every bound of AMP/1 (<c>SPEC.md</c>) and of <c>axiom-mesh-local/1</c> (<c>LOCAL-SPEC.md</c>) as a named constant
/// with the specification's value, so that a bound is written once and enforced where the bytes enter.
/// </summary>
/// <remarks>
/// A constant is compiled into every caller, so its value can never change within a protocol version; the
/// specifications fix them. The full set is listed in <c>Documentation/LIMITS.md</c>; this class grows to hold all of
/// it, and a convention test compares the two.
/// </remarks>
public static class Limits
{
    /// <summary>The smallest frame length, the two bytes of the message id (SPEC.md §5.1, LOCAL-SPEC.md §12).</summary>
    public const int MinFrameLength = 2;

    /// <summary>The largest frame length, 4 MiB + 4 KiB (SPEC.md §5.1, LOCAL-SPEC.md §12).</summary>
    public const int MaxFrameLength = 4_198_400;

    /// <summary>The largest object, 4 MiB (SPEC.md §8.1).</summary>
    public const int MaxObjectLength = 4_194_304;

    /// <summary>The largest Chunk's data, 1 MiB; every Chunk of a file but its last holds exactly this much (SPEC.md §8.2).</summary>
    public const int ChunkLength = 1_048_576;
}
