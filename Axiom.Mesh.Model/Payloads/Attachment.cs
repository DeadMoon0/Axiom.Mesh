namespace Axiom.Mesh;

/// <summary>A file or directory attached to a Message (SPEC.md §8.5).</summary>
/// <param name="Name">The name shown; at most 255 UTF-8 bytes.</param>
/// <param name="Hash">A Tree or a ChunkList.</param>
/// <param name="Size">A copy of its size, for display, as in a Tree (SPEC.md §8.4).</param>
[ClosedShape("The layout of an attachment is fixed by SPEC.md §8.5.")]
public readonly record struct Attachment(string Name, ObjectHash Hash, ulong Size);
