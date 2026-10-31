namespace Axiom.Mesh.Local;

/// <summary>What <see cref="ExistingRule"/> decided to do before a Tree is written.</summary>
internal enum ExistingAction
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Create the directory; nothing is there.</summary>
    Create = 1,

    /// <summary>Write into the directory that is there; other files stay.</summary>
    WriteInto = 2,

    /// <summary>Delete the directory that is there (one materialised earlier), then create it.</summary>
    ReplaceDirectory = 3,

    /// <summary>Delete the regular file that is there, then create the directory.</summary>
    ReplaceFile = 4,

    /// <summary>Write nothing; the decision's status says why.</summary>
    Refuse = 5,
}
