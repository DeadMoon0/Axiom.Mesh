using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// The body of one request of LOCAL-SPEC.md §6 to §10: what a client asks the Host. Every request is a sealed record
/// that also implements <see cref="IRequest{TSelf, TOk}"/>, which names its message id, the roles that may send it and
/// the layout of its OK body. A client sends one with <c>LocalClient.SendAsync</c>, the Host receives it inside a
/// <see cref="RequestFrame"/>.
/// </summary>
public abstract record LocalRequest
{
    private protected LocalRequest()
    {
    }

    /// <summary>The request's message id (§14.1), the same as its static <c>Id</c>.</summary>
    public abstract ushort MessageId { get; }

    /// <summary>Writes the request's body, after the correlation of its frame.</summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="System.ArgumentException">A field is outside its bounds.</exception>
    public abstract void Write(ref WireWriter writer);
}
