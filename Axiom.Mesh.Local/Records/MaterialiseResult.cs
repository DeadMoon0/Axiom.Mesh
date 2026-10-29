using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The result body of a Done <c>Materialise</c> operation (LOCAL-SPEC.md §8.6).</summary>
public sealed record MaterialiseResult : IWireRecord<MaterialiseResult>
{
    /// <summary>Files written.</summary>
    public uint Files { get; init; }

    /// <summary>Bytes written.</summary>
    public ulong Bytes { get; init; }

    /// <summary>Conflicts of SPEC.md §8.4, named in the operation's message.</summary>
    public uint Conflicts { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out MaterialiseResult? value)
    {
        var read = new MaterialiseResult
        {
            Files = reader.UInt32BigEndian(),
            Bytes = reader.UInt64BigEndian(),
            Conflicts = reader.UInt32BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        writer.UInt32BigEndian(Files);
        writer.UInt64BigEndian(Bytes);
        writer.UInt32BigEndian(Conflicts);
    }
}
