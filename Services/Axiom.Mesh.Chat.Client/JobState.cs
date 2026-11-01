namespace Axiom.Mesh.Chat;

/// <summary>Where a job stands (CHAT-SERVICE.md §8).</summary>
public enum JobState : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Work is in progress.</summary>
    Running = 1,

    /// <summary>Ended with its result: the attachment stored, or the path written.</summary>
    Done = 2,

    /// <summary>Ended without it; the job's status and problem say why.</summary>
    Failed = 3,
}
