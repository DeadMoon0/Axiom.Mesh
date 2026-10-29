namespace Axiom.Mesh;

/// <summary>Who has to change something for a failed call to succeed.</summary>
/// <remarks>A peer is never at fault here: what a peer does wrong is a violation or faulty content, never an exception.</remarks>
public enum MeshFault
{
    /// <summary>The code calling Mesh: it passed something the call does not take, or called at the wrong time.</summary>
    Caller = 0,

    /// <summary>The configuration a node or a client was given: a value, a key, a service entry.</summary>
    Configuration = 1,

    /// <summary>Something outside Mesh: a file, a directory, a pipe, another process it depends on.</summary>
    Environment = 2,
}
