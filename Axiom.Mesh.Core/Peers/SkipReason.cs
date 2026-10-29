namespace Axiom.Mesh.Core;

/// <summary>Why the dial policy does not dial an announced device now.</summary>
internal enum SkipReason
{
    /// <summary>Not skipped: dial.</summary>
    None = 0,

    /// <summary>This node has no identity yet, so it could not send its <c>Hello</c>.</summary>
    NoIdentity = 1,

    /// <summary>The announcement is this node's own.</summary>
    Self = 2,

    /// <summary>The device is in no current Roster of a known user (SPEC.md §3): never dialled.</summary>
    UnknownDevice = 3,

    /// <summary>The device's Roster removes it (SPEC.md §8.8): never dialled again.</summary>
    Removed = 4,

    /// <summary>A session to the device exists or is opening (SPEC.md §4.4, §4.5).</summary>
    Connected = 5,

    /// <summary>A dial to the device is under way.</summary>
    Dialling = 6,

    /// <summary>A failed dial, a refusal or repeated violations hold dials back until a time (SPEC.md §4.4, §6.2, §12.1).</summary>
    BackingOff = 7,
}
