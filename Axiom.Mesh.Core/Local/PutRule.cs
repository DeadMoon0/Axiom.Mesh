using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// LOCAL-SPEC.md §9.5 <c>PutObject</c>: the object is named by its hash and checked as SPEC.md §8.1 to §8.4 say for its
/// tag; an unknown tag is opaque and accepted; a malformed object, and any signed one (tags 4 to 7, which are written
/// with <c>WriteEntry</c> or produced by the Host), is <c>Invalid</c>.
/// </summary>
internal static class PutRule
{
    /// <summary>Checks an object a service puts.</summary>
    /// <param name="bytes">The object's bytes, owned by the caller from now on.</param>
    /// <param name="storable">The object, ready to store, when it passes.</param>
    /// <returns><see langword="null"/> when it passes; else why not.</returns>
    public static RequestRefusal? Check(byte[] bytes, out StorableObject? storable)
    {
        storable = null;
        if (!Objects.TryParse(HashedObject.Of(bytes), out ParsedObject? parsed, out ObjectFault fault))
            return new RequestRefusal(Status.Invalid, $"The object is malformed (SPEC.md §8): {fault}.", "object");
        if (parsed is not StorableObject unsigned)
            return new RequestRefusal(Status.Invalid, $"A {parsed.Tag} is signed; Entries are written with WriteEntry and the Host makes the rest.", "object");
        storable = unsigned;
        return null;
    }
}
