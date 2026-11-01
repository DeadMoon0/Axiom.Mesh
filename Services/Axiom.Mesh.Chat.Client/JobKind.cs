namespace Axiom.Mesh.Chat;

/// <summary>What a job does (CHAT-SERVICE.md §8).</summary>
public enum JobKind : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Stores a file or a folder as an attachment (<c>attach</c>).</summary>
    Attach = 1,

    /// <summary>Fetches an attachment and writes it to disk (<c>get</c>).</summary>
    Get = 2,
}
