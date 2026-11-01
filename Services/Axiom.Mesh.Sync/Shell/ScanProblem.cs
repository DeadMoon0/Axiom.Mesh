namespace Axiom.Mesh.Sync;

/// <summary>A path a scan could not take in: a name a Tree cannot hold, a directory too large or unreadable.</summary>
/// <param name="Path">The path relative to the bound directory, with <c>/</c>.</param>
/// <param name="Kind">Why.</param>
internal readonly record struct ScanProblem(string Path, ConflictKind Kind);
