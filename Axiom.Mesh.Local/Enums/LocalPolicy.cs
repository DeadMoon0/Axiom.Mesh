namespace Axiom.Mesh.Local;

/// <summary>The local retention policy of a Package (LOCAL-SPEC.md §14.14).</summary>
/// <remarks>Values 3 to 255 are reserved; a value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum LocalPolicy : byte
{
    /// <summary>The Manifest's retention when it is not 0, else the device class default (SPEC.md §11.2).</summary>
    Default = 0,

    /// <summary>Everything: the desktop row of SPEC.md §11.2.</summary>
    Everything = 1,

    /// <summary>Latest only: the phone row of SPEC.md §11.2.</summary>
    LatestOnly = 2,
}
