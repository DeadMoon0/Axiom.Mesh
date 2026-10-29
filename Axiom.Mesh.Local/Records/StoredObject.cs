using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The OK body of <c>PutObject</c> (LOCAL-SPEC.md §9.5).</summary>
public sealed record StoredObject : IWireRecord<StoredObject>
{
    /// <summary>The object's hash.</summary>
    public ObjectHash Hash { get; init; }

    /// <summary><see langword="false"/> when the object was already stored.</summary>
    public bool Stored { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out StoredObject? value)
    {
        var read = new StoredObject
        {
            Hash = ObjectHash.Read(ref reader),
            Stored = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Hash.Write(ref writer);
        writer.Boolean(Stored);
    }
}
