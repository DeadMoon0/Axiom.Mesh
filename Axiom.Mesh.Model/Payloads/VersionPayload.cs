using System;
using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Payloads;

/// <summary>A version of an Archive's files (PAYLOADS.md §2, kind 1).</summary>
public sealed class VersionPayload : EntryPayload
{
    /// <summary>The root Tree of the version.</summary>
    public required ObjectHash Tree { get; init; }

    /// <summary>The branch name, <c>main</c> by default; never empty, at most 64 bytes.</summary>
    public required string Label { get; init; }

    /// <summary>Empty, or a pre-release tag such as <c>beta.2</c>; at most 64 bytes.</summary>
    public required string Prerelease { get; init; }

    /// <summary>Release notes; at most 4 096 bytes.</summary>
    public required string Notes { get; init; }

    /// <inheritdoc />
    public override EntryKind Kind => EntryKind.Version;

    /// <inheritdoc />
    public override IReadOnlyList<ObjectHash> References => [Tree];

    internal static VersionPayload Read(ref WireReader reader)
    {
        var payload = new VersionPayload
        {
            Tree = ObjectHash.Read(ref reader),
            Label = Wire.ReadString(ref reader, Limits.MaxVersionLabelLength),
            Prerelease = Wire.ReadString(ref reader, Limits.MaxVersionPrereleaseLength),
            Notes = Wire.ReadString(ref reader, Limits.MaxVersionNotesLength),
        };

        if (payload.Label.Length == 0)
            reader.Fail();
        return payload;
    }

    internal void Write(ref WireWriter writer)
    {
        if (Label.Length == 0)
            throw new ArgumentException("A Version's label must not be empty (PAYLOADS.md §2).", nameof(Label));

        Tree.Write(ref writer);
        Wire.WriteString(ref writer, Label, Limits.MaxVersionLabelLength);
        Wire.WriteString(ref writer, Prerelease, Limits.MaxVersionPrereleaseLength);
        Wire.WriteString(ref writer, Notes, Limits.MaxVersionNotesLength);
    }
}
