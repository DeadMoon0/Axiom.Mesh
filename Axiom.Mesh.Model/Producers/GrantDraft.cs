using System;
using System.Buffers;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>A Grant to sign (SPEC.md §8.9): the Package's owner states what a user may do with it.</summary>
public sealed class GrantDraft
{
    /// <summary>The grantee's user key; the grantee id derives from it.</summary>
    public required PublicKey Grantee { get; init; }

    /// <summary>What the grantee may do; <see cref="GrantRights.None"/> revokes.</summary>
    public required GrantRights Rights { get; init; }

    /// <summary>The sequence number, higher than every earlier Grant of the Package to the grantee.</summary>
    public required ulong Seq { get; init; }

    /// <summary>Encodes and signs the Grant with the owner's user key.</summary>
    /// <param name="owner">The owner's signer.</param>
    /// <param name="manifest">The Package's current Manifest, which names the owner.</param>
    /// <returns>The Grant, verified against <paramref name="manifest"/>.</returns>
    /// <exception cref="ArgumentException">The signer is not the Manifest's owner, the grantee is the owner, or the rights are undefined.</exception>
    public VerifiedGrant Sign(ISigner owner, VerifiedManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(Grantee);
        if (!owner.PublicKey.Equals(manifest.Content.OwnerKey))
            throw new ArgumentException("Only the Package's owner signs its Grants (SPEC.md §9.1); the signer's key is not the Manifest's owner key.", nameof(owner));
        if (Grantee.Equals(manifest.Content.OwnerKey))
            throw new ArgumentException("The owner needs no Grant (SPEC.md §8.9).", nameof(Grantee));
        if (!Enum.IsDefined(Rights))
            throw new ArgumentException($"Rights {(byte)Rights} are not defined (SPEC.md §8.9).", nameof(Rights));

        ArrayBufferWriter<byte> output = ObjectEncoding.Start(ObjectTag.Grant);
        var writer = new WireWriter(output);
        manifest.Content.Package.Write(ref writer);
        Grantee.AsUserId().Write(ref writer);
        Grantee.Write(ref writer);
        writer.UInt8((byte)Rights);
        writer.UInt64BigEndian(Seq);

        UnverifiedGrant grant = ObjectEncoding.ParseOwn<UnverifiedGrant>(ObjectEncoding.Sign(output, SigningDomain.Grant, owner));
        return VerifiedGrant.TryVerify(grant, manifest, out VerifiedGrant? verified)
            ? verified
            : throw ObjectEncoding.SignerDefect("Grant");
    }
}
