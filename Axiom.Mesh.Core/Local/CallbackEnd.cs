namespace Axiom.Mesh.Core;

/// <summary>How a callback toward a service ended (LOCAL-SPEC.md §3.6, §9.8), which decides what its caller hears.</summary>
internal enum CallbackEnd
{
    /// <summary>Not stated: the value of an uninitialised field; never the end of a callback that was issued.</summary>
    Unknown = 0,

    /// <summary>The service's <c>Reply</c> arrived within the deadline.</summary>
    Replied = 1,

    /// <summary>The deadline passed first: the caller hears <c>ServiceTimeout</c> and a later reply is ignored.</summary>
    Expired = 2,

    /// <summary>The service's connection ended first: the caller hears <c>ServiceUnavailable</c>.</summary>
    ConnectionEnded = 3,
}
