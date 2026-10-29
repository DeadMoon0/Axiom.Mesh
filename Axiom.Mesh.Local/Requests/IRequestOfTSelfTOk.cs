using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// What a request states about itself in LOCAL-SPEC.md: its message id (§14.1), its name, the roles that may send it
/// and the layout of its OK body. The type of the OK body is part of the request's type, so
/// <c>client.SendAsync(new GetIdentity(), ct)</c> is a <c>LocalResult&lt;Identity&gt;</c> without naming it.
/// </summary>
/// <typeparam name="TSelf">The request itself.</typeparam>
/// <typeparam name="TOk">The OK body of its <c>Response</c>.</typeparam>
public interface IRequest<TSelf, TOk> : IWireRecord<TSelf>
    where TSelf : LocalRequest, IRequest<TSelf, TOk>
{
    /// <summary>The message id, in the request range 256 to 1023 (§3.3, §14.1).</summary>
    static abstract ushort Id { get; }

    /// <summary>The request's name as §14.1 writes it.</summary>
    static abstract string RequestName { get; }

    /// <summary>The roles that may send it; any other is answered <c>NotPermitted</c> (§3.4).</summary>
    static abstract Roles AllowedRoles { get; }

    /// <summary>Reads the OK body of the request's <c>Response</c>.</summary>
    /// <param name="reader">The reader, positioned after the status.</param>
    /// <param name="ok">The OK body.</param>
    /// <returns><see langword="true"/> when every field was in its bounds.</returns>
    static abstract bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out TOk ok);

    /// <summary>Writes the OK body of the request's <c>Response</c>.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="ok">The OK body.</param>
    /// <exception cref="System.ArgumentException">A field is outside its bounds.</exception>
    static abstract void WriteOk(ref WireWriter writer, TOk ok);
}
