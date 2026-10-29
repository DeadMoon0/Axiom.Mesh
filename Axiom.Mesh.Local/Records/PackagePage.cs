using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A page of <c>ListPackages</c> (LOCAL-SPEC.md §6.4).</summary>
public sealed record PackagePage : IWireRecord<PackagePage>
{
    /// <summary>The Packages, ascending by id.</summary>
    public IReadOnlyList<Package> Packages { get; init; } = [];

    /// <summary>Whether the page was cut; continue after its last Package.</summary>
    public bool More { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out PackagePage? value)
    {
        var read = new PackagePage
        {
            Packages = LocalWire.ReadList<Package>(ref reader, FieldBounds.PackagesCount),
            More = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        LocalWire.WriteList(ref writer, Packages, FieldBounds.PackagesCount);
        writer.Boolean(More);
    }
}
