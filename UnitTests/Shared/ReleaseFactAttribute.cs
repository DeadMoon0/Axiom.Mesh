namespace Axiom.Mesh.Tests.Shared;

/// <summary>
/// A fact that only runs in Release builds. Debug builds compile async state machines as classes, so every async call
/// allocates there and allocation counts say nothing about the shipped code.
/// </summary>
public sealed class ReleaseFactAttribute : FactAttribute
{
    /// <summary>Skips the fact outside Release builds.</summary>
    public ReleaseFactAttribute()
    {
#if DEBUG
        Skip = "Allocation counts are only meaningful in Release builds.";
#endif
    }
}
