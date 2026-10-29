namespace Axiom.Mesh.Core;

/// <summary>Why an object is wanted: for which Package and by which step; the Package becomes intake's context.</summary>
/// <param name="Package">The Package it is wanted for; zero for a Roster.</param>
/// <param name="Kind">The step that asked for it.</param>
internal readonly record struct WantReason(PackageId Package, WantKind Kind);
