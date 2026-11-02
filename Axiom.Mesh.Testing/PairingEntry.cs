namespace Axiom.Mesh.Testing;

/// <summary>How the entering side of a pairing names the showing one (PAIR-SPEC.md §2): the code it typed, or the device discovery announced.</summary>
public enum PairingEntry
{
    /// <summary>The code the showing side printed, entered as text (PAIR-SPEC.md §2.1).</summary>
    ByCode = 0,

    /// <summary>The showing device's id and addresses as <c>ListDiscovered</c> shows them on the entering node (PAIR-SPEC.md §2.4).</summary>
    ByDiscovery = 1,
}
