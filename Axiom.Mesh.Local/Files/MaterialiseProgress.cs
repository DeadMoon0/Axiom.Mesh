namespace Axiom.Mesh.Local;

/// <summary>How far <see cref="StoreFiles.MaterialiseAsync"/> got.</summary>
/// <param name="Done">Bytes written so far.</param>
/// <param name="Total">Bytes to write, by the sizes the Trees state.</param>
/// <param name="File">The file being written, relative with <c>/</c>.</param>
[ClosedShape("A progress report: bytes done of bytes in all, and the file at hand.")]
public readonly record struct MaterialiseProgress(ulong Done, ulong Total, string File);
