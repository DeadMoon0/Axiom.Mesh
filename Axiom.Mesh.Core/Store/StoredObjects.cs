using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// Reads back the signed objects the index names as current (Rosters, Manifests, Grants) when the store opens. They are
/// checked again, hash, form and signature, because the only way to a verified object is verification; a file that is
/// missing or does not check stops the start with the steps out.
/// </summary>
internal static class StoredObjects
{
    /// <summary>A stored Roster.</summary>
    /// <param name="files">The object files.</param>
    /// <param name="hash">The Roster's hash.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The verified Roster.</returns>
    /// <exception cref="MeshStoreException">The file is missing or does not verify.</exception>
    public static async Task<VerifiedRoster> RosterAsync(IObjectFiles files, ObjectHash hash, CancellationToken cancellationToken) =>
        await ParseAsync<UnverifiedRoster>(files, hash, "Roster", cancellationToken).ConfigureAwait(false) is var parsed && VerifiedRoster.TryVerify(parsed, out VerifiedRoster? roster)
            ? roster
            : throw Damaged(hash, "Roster");

    /// <summary>A stored Manifest.</summary>
    /// <param name="files">The object files.</param>
    /// <param name="hash">The Manifest's hash.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The verified Manifest.</returns>
    /// <exception cref="MeshStoreException">The file is missing or does not verify.</exception>
    public static async Task<VerifiedManifest> ManifestAsync(IObjectFiles files, ObjectHash hash, CancellationToken cancellationToken) =>
        await ParseAsync<UnverifiedManifest>(files, hash, "Manifest", cancellationToken).ConfigureAwait(false) is var parsed && VerifiedManifest.TryVerify(parsed, out VerifiedManifest? manifest)
            ? manifest
            : throw Damaged(hash, "Manifest");

    /// <summary>A stored Grant.</summary>
    /// <param name="files">The object files.</param>
    /// <param name="hash">The Grant's hash.</param>
    /// <param name="manifest">The current Manifest of its Package.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The verified Grant.</returns>
    /// <exception cref="MeshStoreException">The file is missing or does not verify.</exception>
    public static async Task<VerifiedGrant> GrantAsync(IObjectFiles files, ObjectHash hash, VerifiedManifest manifest, CancellationToken cancellationToken) =>
        await ParseAsync<UnverifiedGrant>(files, hash, "Grant", cancellationToken).ConfigureAwait(false) is var parsed && parsed.Content.Package == manifest.Content.Package
        && VerifiedGrant.TryVerify(parsed, manifest, out VerifiedGrant? grant)
            ? grant
            : throw Damaged(hash, "Grant");

    /// <summary>Every byte of a stored object, or <see langword="null"/> when its file is gone.</summary>
    /// <param name="files">The object files.</param>
    /// <param name="hash">The object's hash.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The bytes, or <see langword="null"/>.</returns>
    public static async Task<byte[]?> ReadAsync(IObjectFiles files, ObjectHash hash, CancellationToken cancellationToken)
    {
        if (!files.TryOpenRead(hash, out Stream? stream))
            return null;

        await using (stream.ConfigureAwait(false))
        {
            byte[] bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return bytes;
        }
    }

    private static async Task<T> ParseAsync<T>(IObjectFiles files, ObjectHash hash, string what, CancellationToken cancellationToken)
        where T : ParsedObject
    {
        byte[] bytes = await ReadAsync(files, hash, cancellationToken).ConfigureAwait(false) ?? throw Damaged(hash, what);
        return HashedObject.TryVerify(bytes, hash, out HashedObject hashed) && Objects.TryParse(hashed, out ParsedObject? parsed, out _) && parsed is T typed
            ? typed
            : throw Damaged(hash, what);
    }

    private static MeshStoreException Damaged(ObjectHash hash, string what) => new(
        $"The node's index names {what} {hash} as current, but its object file is missing or damaged.",
        "Restore the data directory from a backup taken while the node was stopped.",
        "Or move the data directory aside and start anew: the node then fetches what it follows from its peers, and clients see a new store epoch.");
}
