namespace Axiom.Mesh.Core.Storage;

/// <summary>What became of an object file the store tried to delete (LOCAL-SPEC.md §9.7).</summary>
public enum ObjectDeletion
{
    /// <summary>No file was there: never stored, or already gone.</summary>
    Absent = 0,

    /// <summary>The name is gone; readers holding the file keep its bytes.</summary>
    Deleted = 1,

    /// <summary>A reader holds the file without allowing deletion; the store keeps the object and evicts it later.</summary>
    InUse = 2,
}
