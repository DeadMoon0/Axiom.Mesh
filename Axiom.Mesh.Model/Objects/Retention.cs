namespace Axiom.Mesh;

/// <summary>The owner's retention hint in a Manifest (SPEC.md §8.7); any other value is read as <see cref="Default"/>.</summary>
public enum Retention : byte
{
    /// <summary>The device class decides (SPEC.md §11.2).</summary>
    Default = 0,

    /// <summary>Keep everything.</summary>
    Everything = 1,

    /// <summary>Keep the latest only.</summary>
    LatestOnly = 2,
}
