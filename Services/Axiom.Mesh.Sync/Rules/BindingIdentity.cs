using System;
using System.Text;

namespace Axiom.Mesh.Sync;

/// <summary>
/// A binding's id (SYNC-SERVICE.md §2): the first 16 bytes of SHA-256 over a domain, the bound directory and the Archive, so
/// binding the same directory to the same Archive again names the same binding, and no randomness is needed. Pure.
/// </summary>
internal static class BindingIdentity
{
    private const string Domain = "axiom-mesh-sync/binding/1";

    /// <summary>The id of a binding.</summary>
    /// <param name="directory">The bound directory, absolute and normalised.</param>
    /// <param name="package">The Archive.</param>
    /// <returns>16 bytes.</returns>
    public static byte[] Of(string directory, PackageId package)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        byte[] domain = Encoding.UTF8.GetBytes(Domain);
        byte[] path = Encoding.UTF8.GetBytes(directory);
        byte[] input = new byte[domain.Length + 1 + path.Length + Hash32.Length];
        domain.CopyTo(input, 0);
        path.CopyTo(input, domain.Length + 1);
        package.Value.CopyTo(input.AsSpan(domain.Length + 1 + path.Length));
        byte[] full = new byte[Hash32.Length];
        Hash32.Of(input).CopyTo(full);
        return full[..16];
    }
}
