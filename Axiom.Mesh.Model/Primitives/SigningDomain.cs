using System;
using System.Text;

namespace Axiom.Mesh;

/// <summary>
/// The domain string a signature over one kind of structure is separated by (SPEC.md §2.2, §14.5): the four signed
/// objects each have their own, so a signature over one can never be replayed as a signature over another.
/// </summary>
/// <remarks>
/// A closed set: the only instances are the four static properties, so a digest cannot be computed under a domain
/// SPEC.md does not name. The Package id's hash domain is not a signing domain; it is <see cref="PackageId.DerivationDomain"/>.
/// </remarks>
public sealed class SigningDomain
{
    private readonly byte[] utf8;

    private SigningDomain(string name)
    {
        Name = name;
        utf8 = Encoding.UTF8.GetBytes(name);
    }

    /// <summary><c>axiom-mesh/entry/1</c>, signed by the writing device (SPEC.md §8.6).</summary>
    public static SigningDomain Entry { get; } = new("axiom-mesh/entry/1");

    /// <summary><c>axiom-mesh/manifest/1</c>, signed by the owner's user key (SPEC.md §8.7).</summary>
    public static SigningDomain Manifest { get; } = new("axiom-mesh/manifest/1");

    /// <summary><c>axiom-mesh/roster/1</c>, signed by the user key (SPEC.md §8.8).</summary>
    public static SigningDomain Roster { get; } = new("axiom-mesh/roster/1");

    /// <summary><c>axiom-mesh/grant/1</c>, signed by the owner's user key (SPEC.md §8.9).</summary>
    public static SigningDomain Grant { get; } = new("axiom-mesh/grant/1");

    /// <summary>The domain string as SPEC.md §14.5 writes it.</summary>
    public string Name { get; }

    /// <summary>The domain string as UTF-8 without a terminator, the bytes that precede the body in the digest.</summary>
    public ReadOnlySpan<byte> Utf8 => utf8;

    /// <inheritdoc />
    public override string ToString() => Name;
}
