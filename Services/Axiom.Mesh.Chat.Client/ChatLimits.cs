namespace Axiom.Mesh.Chat;

/// <summary>The bounds of <c>axiom-mesh-chat/1</c> (CHAT-SERVICE.md §9, LIMITS.md "Chat namespace").</summary>
public static class ChatLimits
{
    /// <summary>The most messages one <c>timeline</c> or <c>thread</c> reply carries.</summary>
    public const int TimelinePage = 256;

    /// <summary>The most UTF-8 bytes of a message's text a <c>message</c> event carries.</summary>
    public const int PreviewLength = 256;

    /// <summary>The most jobs running at once; one more is <c>Busy</c>.</summary>
    public const int MaxJobs = 16;

    /// <summary>The ended jobs the service remembers; an older one is forgotten when one more ends.</summary>
    public const int EndedJobsKept = 48;

    /// <summary>The most jobs <c>jobs</c> lists: every running one and the ended ones kept.</summary>
    public const int JobsListed = MaxJobs + EndedJobsKept;

    /// <summary>The most Channels <c>channels</c> lists.</summary>
    public const int Channels = 256;

    /// <summary>The most bytes of a reply or an event's payload (LOCAL-SPEC.md §3.2 <c>payload</c>); a timeline page is cut to fit.</summary>
    public const int PayloadBytes = Limits.MaxPayloadLength;

    /// <summary>The most bytes of a message's text (PAYLOADS.md §3).</summary>
    public const int TextLength = Limits.MaxMessageTextLength;

    /// <summary>The most attachments of a message (PAYLOADS.md §3).</summary>
    public const int Attachments = Limits.MaxMessageAttachments;

    /// <summary>The most bytes of an attachment's name (PAYLOADS.md §3).</summary>
    public const int AttachmentNameLength = Limits.MaxAttachmentNameLength;

    /// <summary>The most bytes of a path (LOCAL-SPEC.md §3.2).</summary>
    public const int PathLength = 4_096;

    /// <summary>The most bytes of a user's name (LOCAL-SPEC.md §5.2).</summary>
    public const int UserNameLength = 64;

    /// <summary>The most bytes of a device's name (SPEC.md §8.8).</summary>
    public const int DeviceNameLength = Limits.MaxDeviceNameLength;

    /// <summary>The most bytes of a Channel's name (SPEC.md §8.7).</summary>
    public const int ChannelNameLength = Limits.MaxManifestNameLength;

    /// <summary>The most bytes of a job's problem, as of an <c>Error.message</c> (LOCAL-SPEC.md §5.14).</summary>
    public const int ProblemLength = 256;

    /// <summary>The most <c>replyTo</c> steps a <c>thread</c> follows up to its root.</summary>
    public const int ThreadDepth = 64;

    /// <summary>The most bytes of the namespace and the version <c>about</c> carries (LOCAL-SPEC.md §3.2 <c>name</c>).</summary>
    public const int NameLength = 64;
}
