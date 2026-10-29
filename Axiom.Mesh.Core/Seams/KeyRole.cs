namespace Axiom.Mesh.Core;

/// <summary>Which of a device's two private keys a <see cref="IKeyStore"/> holds (SPEC.md §2.3).</summary>
public enum KeyRole
{
    /// <summary>No key: the value of an uninitialised field, never stored.</summary>
    None = 0,

    /// <summary>The device key: it signs this device's Entries and is the key of its TLS certificate.</summary>
    Device = 1,

    /// <summary>The user key: it signs the user's Rosters, Manifests and Grants; held by one device of the user only.</summary>
    User = 2,
}
