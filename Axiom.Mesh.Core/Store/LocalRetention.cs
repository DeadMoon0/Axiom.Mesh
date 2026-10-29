namespace Axiom.Mesh.Core;

/// <summary>The local retention policy of a Package (LOCAL-SPEC.md §14.14, whose values these are).</summary>
internal enum LocalRetention : byte
{
    /// <summary>The Manifest's retention when it is not 0, else the device class's default (SPEC.md §11.2).</summary>
    Default = 0,

    /// <summary>Everything: the desktop row of SPEC.md §11.2.</summary>
    Everything = 1,

    /// <summary>Latest only: the phone row of SPEC.md §11.2.</summary>
    LatestOnly = 2,
}
