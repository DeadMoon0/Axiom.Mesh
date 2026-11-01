namespace Axiom.Mesh.Sync;

/// <summary>A copy a merge kept beside a path both leaves changed differently (SPEC.md §9.5).</summary>
/// <param name="Path">The path whose state the later leaf decided.</param>
/// <param name="CopyPath">Where the other side's file, or whole subtree, lies.</param>
/// <param name="OtherDevice">The device whose leaf lost.</param>
internal readonly record struct ConflictCopy(string Path, string CopyPath, DeviceId OtherDevice);
