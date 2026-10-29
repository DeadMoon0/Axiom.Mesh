using System;
using System.Diagnostics.CodeAnalysis;

namespace Axiom.Mesh;

/// <summary>
/// An Entry whose signature verifies against its writing device's key (SPEC.md §8.6). Not yet storable: whether that
/// device may write to the Package is decided by <see cref="EntryAdmission.Decide"/>.
/// </summary>
public sealed class VerifiedEntry
{
    private VerifiedEntry(UnverifiedEntry entry)
    {
        Hash = entry.Hash;
        Bytes = entry.Bytes;
        Content = entry.Content;
    }

    /// <summary>The Entry's object hash.</summary>
    public ObjectHash Hash { get; }

    /// <summary>Every byte of the Entry.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>What the bytes say; the writing device signed it.</summary>
    public EntryContent Content { get; }

    /// <summary>
    /// Checks the Entry's signature over the bytes received against the writing device's key, with domain
    /// <c>axiom-mesh/entry/1</c> (SPEC.md §2.2, §8.6).
    /// </summary>
    /// <param name="entry">The parsed Entry.</param>
    /// <param name="deviceKey">The key of the device the Entry names, from the roster that lists it.</param>
    /// <param name="verified">The verified Entry, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the signature does not verify: the Entry is invalid.</returns>
    /// <exception cref="ArgumentException"><paramref name="deviceKey"/> is not the key of the Entry's device; the caller looked up the wrong key.</exception>
    public static bool TryVerify(UnverifiedEntry entry, PublicKey deviceKey, [NotNullWhen(true)] out VerifiedEntry? verified)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(deviceKey);
        if (deviceKey.AsDeviceId() != entry.Content.Device)
            throw new ArgumentException($"The key given is device {deviceKey.AsDeviceId()}'s, but the Entry names device {entry.Content.Device}.", nameof(deviceKey));

        bool valid = deviceKey.Verify(Digests.Of(SigningDomain.Entry, entry.SignedBytes), entry.Content.Signature);
        verified = valid ? new VerifiedEntry(entry) : null;
        return valid;
    }

    internal AdmittedEntry Admit() => new(Hash, Bytes, Content);
}
