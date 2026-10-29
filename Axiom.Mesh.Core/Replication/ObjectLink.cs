namespace Axiom.Mesh.Core;

/// <summary>
/// One object a stored object names (SPEC.md §10.3): with its tag where the naming object says it (a Version's Tree, a
/// Tree entry's mode, a ChunkList's Chunks), else <see cref="ObjectTag.Unassigned"/> (a Message attachment is a Tree or a
/// ChunkList).
/// </summary>
/// <param name="Hash">The object named.</param>
/// <param name="Tag">Its tag as the naming object says it, or <see cref="ObjectTag.Unassigned"/>.</param>
internal readonly record struct ObjectLink(ObjectHash Hash, ObjectTag Tag);
