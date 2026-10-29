using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>What a callback states about itself in LOCAL-SPEC.md: its message id (§14.1) and its reply body (§9.8).</summary>
/// <typeparam name="TSelf">The callback itself.</typeparam>
/// <typeparam name="TReply">The body of a successful <c>Reply</c>.</typeparam>
public interface ICallback<TSelf, TReply> : IWireRecord<TSelf>
    where TSelf : LocalCallback, ICallback<TSelf, TReply>
{
    /// <summary>The message id, in the callback range 1536 to 2047 (§3.3, §14.1).</summary>
    static abstract ushort Id { get; }

    /// <summary>Reads the body of a successful <c>Reply</c>.</summary>
    /// <param name="reader">The reader, positioned after the status.</param>
    /// <param name="reply">The reply body.</param>
    /// <returns><see langword="true"/> when every field was in its bounds.</returns>
    static abstract bool TryReadReply(ref WireReader reader, [MaybeNullWhen(false)] out TReply reply);

    /// <summary>Writes the body of a successful <c>Reply</c>.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="reply">The reply body.</param>
    /// <exception cref="System.ArgumentException">A field is outside its bounds.</exception>
    static abstract void WriteReply(ref WireWriter writer, TReply reply);
}
