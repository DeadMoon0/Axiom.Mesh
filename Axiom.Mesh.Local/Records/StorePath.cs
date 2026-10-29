using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The OK body of <c>GetStorePath</c> (LOCAL-SPEC.md §9.7).</summary>
public sealed record StorePath : IWireRecord<StorePath>
{
    /// <summary>The store directory.</summary>
    public string Path { get; init; } = "";

    /// <summary>The directory layout; 1, objects at <c>objects/h[0..1]/h[2..3]/h</c>.</summary>
    public byte Layout { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out StorePath? value)
    {
        var read = new StorePath
        {
            Path = Wire.ReadString(ref reader, FieldBounds.PathLength),
            Layout = reader.UInt8(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Path, FieldBounds.PathLength);
        writer.UInt8(Layout);
    }
}
