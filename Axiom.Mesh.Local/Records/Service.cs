using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A configured service (LOCAL-SPEC.md §5.12).</summary>
public sealed record Service : IWireRecord<Service>
{
    /// <summary>The service's name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Whether it is connected.</summary>
    public bool Connected { get; init; }

    /// <summary>Whether it runs inside the Host's process.</summary>
    public bool InProcess { get; init; }

    /// <summary>The version from its <c>Hello</c>; empty when not connected.</summary>
    public string Version { get; init; } = "";

    /// <summary>Whether the Host spawns and supervises it.</summary>
    public bool Spawned { get; init; }

    /// <summary>The Package kinds its configuration lets it register, ascending.</summary>
    public IReadOnlyList<PackageKind> Kinds { get; init; } = [];

    /// <summary>The Entry kinds its configuration lets it register, ascending.</summary>
    public IReadOnlyList<EntryKind> EntryKinds { get; init; } = [];

    /// <summary>Whether the service is external (§4.2): neither in-process nor spawned, it presents its configured token.</summary>
    public bool IsExternal => !InProcess && !Spawned;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Service? value)
    {
        var read = new Service
        {
            Name = Wire.ReadString(ref reader, FieldBounds.NameLength),
            Connected = reader.Boolean(),
            InProcess = reader.Boolean(),
            Version = Wire.ReadString(ref reader, FieldBounds.VersionLength),
            Spawned = reader.Boolean(),
            Kinds = LocalWire.ReadPackageKinds(ref reader, FieldBounds.ServiceKindsCount),
            EntryKinds = LocalWire.ReadEntryKinds(ref reader, FieldBounds.ServiceEntryKindsCount),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Name, FieldBounds.NameLength);
        writer.Boolean(Connected);
        writer.Boolean(InProcess);
        Wire.WriteString(ref writer, Version, FieldBounds.VersionLength);
        writer.Boolean(Spawned);
        LocalWire.WritePackageKinds(ref writer, Kinds, FieldBounds.ServiceKindsCount);
        LocalWire.WriteEntryKinds(ref writer, EntryKinds, FieldBounds.ServiceEntryKindsCount);
    }
}
