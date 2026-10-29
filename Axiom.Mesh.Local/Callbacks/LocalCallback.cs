using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// The body of one callback of LOCAL-SPEC.md §9.8: what the Host asks a service and needs an answer to. Every callback
/// is a sealed record that also implements <see cref="ICallback{TSelf, TReply}"/>. The Host sends one inside a
/// <see cref="Callback"/>; the service answers with a <see cref="Reply"/>.
/// </summary>
public abstract record LocalCallback
{
    private protected LocalCallback()
    {
    }

    /// <summary>The callback's message id (§14.1), the same as its static <c>Id</c>.</summary>
    public abstract ushort MessageId { get; }

    /// <summary>Writes the callback's body, after the callback id of its frame.</summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="System.ArgumentException">A field is outside its bounds.</exception>
    public abstract void Write(ref WireWriter writer);
}
