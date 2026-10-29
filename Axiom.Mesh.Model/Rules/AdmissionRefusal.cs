namespace Axiom.Mesh;

/// <summary>Why a verified Entry is invalid (SPEC.md §8.6, §9.6): faulty content, dropped and never forwarded.</summary>
public enum AdmissionRefusal
{
    /// <summary>The device is removed in its user's current Roster (SPEC.md §8.8).</summary>
    DeviceRemoved = 0,

    /// <summary>The device's user is neither the owner nor holds a current <c>Write</c> Grant (SPEC.md §9.6).</summary>
    NotAWriter = 1,
}
