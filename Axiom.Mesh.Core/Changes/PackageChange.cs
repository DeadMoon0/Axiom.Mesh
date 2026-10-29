namespace Axiom.Mesh.Core;

/// <summary>One change of a Package's listable state (SPEC.md §7.3; LOCAL-SPEC.md §14.12).</summary>
/// <param name="Package">The Package.</param>
/// <param name="Kind">What changed.</param>
/// <param name="Origin">Who caused it; a push goes to every peer but this one.</param>
internal sealed record PackageChange(PackageId Package, PackageChangeKind Kind, Origin Origin);
