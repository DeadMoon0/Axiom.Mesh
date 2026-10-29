using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// A structure of LOCAL-SPEC.md that reads and writes itself in its canonical layout (§3.2, §5): a record, or the body
/// of a request, an event or a callback.
/// </summary>
/// <typeparam name="TSelf">The structure itself.</typeparam>
/// <remarks>
/// Reading never throws: a value outside its bounds (a length, a count, a <c>bool</c>, an <c>optional</c>, UTF-8) fails
/// the reader and the structure is not made. Values in bounds but wrong in substance (an enumerated value nobody
/// defined, a relative path) are read as they are; judging them is the receiver's job (§1.5, §3.2). Writing refuses a
/// value outside its bounds with an <see cref="System.ArgumentException"/>, so what this side sends is well-formed.
/// </remarks>
public interface IWireRecord<TSelf>
    where TSelf : class, IWireRecord<TSelf>
{
    /// <summary>Reads the structure from <paramref name="reader"/>.</summary>
    /// <param name="reader">The reader, positioned at the structure.</param>
    /// <param name="value">The structure, or <see langword="null"/> when the reader failed.</param>
    /// <returns><see langword="true"/> when every field was in its bounds.</returns>
    static abstract bool TryRead(ref WireReader reader, [NotNullWhen(true)] out TSelf? value);

    /// <summary>Writes the structure.</summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="System.ArgumentException">A field is outside its bounds.</exception>
    void Write(ref WireWriter writer);
}
