namespace Axiom.Mesh.Core;

/// <summary>
/// A Roster, Manifest or Grant the node holds as current (SPEC.md §8.7–§8.9), by what it is about: what answering a
/// <c>Want</c> for its hash checks visibility against (§9.7, §9.8).
/// </summary>
/// <param name="Tag">Roster, Manifest or Grant.</param>
/// <param name="Package">The Package of a Manifest or Grant; zero for a Roster.</param>
/// <param name="User">The user of a Roster, the grantee of a Grant; zero for a Manifest.</param>
internal readonly record struct CurrentSigned(ObjectTag Tag, PackageId Package, UserId User);
