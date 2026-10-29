using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// A Manifest to sign (SPEC.md §8.7). The Package id and owner id are derived from the owner's key and the nonce, so a
/// draft cannot name a Package its owner could not have made; branches are sorted for the caller.
/// </summary>
public sealed class ManifestDraft
{
    /// <summary>The nonce the Package id derives from: 32 random bytes chosen once, when the Package is created.</summary>
    public required PackageNonce Nonce { get; init; }

    /// <summary>The Package kind; it never changes between Manifests of one Package.</summary>
    public required PackageKind Kind { get; init; }

    /// <summary>The sequence number, higher than every earlier Manifest of the Package.</summary>
    public required ulong Seq { get; init; }

    /// <summary>The Package's name; at most 255 UTF-8 bytes.</summary>
    public required string Name { get; init; }

    /// <summary>Milliseconds since the Unix epoch when the Package was created; informational.</summary>
    public required ulong Created { get; init; }

    /// <summary>The owner's retention hint.</summary>
    public Retention Retention { get; init; }

    /// <summary>The branches, in any order; names unique.</summary>
    public IReadOnlyList<Branch> Branches { get; init; } = [];

    /// <summary>Encodes and signs the Manifest with the owner's user key.</summary>
    /// <param name="owner">The owner's signer.</param>
    /// <returns>The Manifest, verified.</returns>
    /// <exception cref="ArgumentException">A field breaks its bound, or two branches share a name.</exception>
    public VerifiedManifest Sign(ISigner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(Branches);
        if (!Enum.IsDefined(Retention))
            throw new ArgumentException($"Retention {(byte)Retention} is not defined (SPEC.md §8.7).", nameof(Retention));

        Branch[] branches = [.. Branches.OrderBy(b => Encoding.UTF8.GetBytes(b.Name), ByteStrings.Comparer)];
        for (int i = 1; i < branches.Length; i++)
        {
            if (branches[i - 1].Name == branches[i].Name)
                throw new ArgumentException($"Two branches are named {branches[i].Name}; names are unique (SPEC.md §8.7).", nameof(Branches));
        }

        UserId ownerId = owner.PublicKey.AsUserId();
        ArrayBufferWriter<byte> output = ObjectEncoding.Start(ObjectTag.Manifest);
        var writer = new WireWriter(output);
        PackageId.Derive(ownerId, Nonce).Write(ref writer);
        ownerId.Write(ref writer);
        owner.PublicKey.Write(ref writer);
        Nonce.Write(ref writer);
        writer.UInt16BigEndian((ushort)Kind);
        writer.UInt64BigEndian(Seq);
        Wire.WriteString(ref writer, Name, Limits.MaxManifestNameLength);
        writer.UInt64BigEndian(Created);
        writer.UInt8((byte)Retention);
        Wire.WriteListCount(ref writer, branches.Length, Limits.MaxManifestBranches);
        foreach (Branch branch in branches)
        {
            Wire.WriteString(ref writer, branch.Name, Limits.MaxBranchNameLength);
            branch.Head.Write(ref writer);
        }

        UnverifiedManifest manifest = ObjectEncoding.ParseOwn<UnverifiedManifest>(ObjectEncoding.Sign(output, SigningDomain.Manifest, owner));
        return VerifiedManifest.TryVerify(manifest, out VerifiedManifest? verified)
            ? verified
            : throw ObjectEncoding.SignerDefect("Manifest");
    }
}
