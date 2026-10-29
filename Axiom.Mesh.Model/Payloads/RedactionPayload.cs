using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>The withdrawal of an earlier Entry of the same Channel (SPEC.md §8.5, kind 3; §9.4).</summary>
public sealed class RedactionPayload : EntryPayload
{
    /// <summary>The Entry withdrawn.</summary>
    public required ObjectHash Target { get; init; }

    /// <inheritdoc />
    public override EntryKind Kind => EntryKind.Redaction;

    /// <inheritdoc />
    public override IReadOnlyList<ObjectHash> References => [];

    internal static RedactionPayload Read(ref WireReader reader) => new() { Target = ObjectHash.Read(ref reader) };

    internal void Write(ref WireWriter writer) => Target.Write(ref writer);
}
