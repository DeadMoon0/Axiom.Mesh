namespace Axiom.Mesh.Local;

/// <summary>One file of a <see cref="MaterialisePlan"/>.</summary>
/// <param name="Path">Its path relative to the target directory, names separated by <c>/</c>.</param>
/// <param name="Hash">A ChunkList for a file, a Chunk holding the target for a link.</param>
/// <param name="Kind">How it is written.</param>
/// <param name="Size">The size the Tree states, for progress; the object is authoritative.</param>
internal readonly record struct PlannedFile(string Path, ObjectHash Hash, PlannedFileKind Kind, ulong Size);
