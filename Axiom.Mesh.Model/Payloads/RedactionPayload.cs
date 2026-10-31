using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Payloads;

/// <summary>The withdrawal of an earlier Entry of the same Channel (PAYLOADS.md §4, kind 3; SPEC.md §9.4); the node acts on the Entry's <c>redacts</c>, which <see cref="Redacts"/> fills.</summary>
public sealed class RedactionPayload : EntryPayload
{
    /// <summary>The Entry withdrawn.</summary>
    public required ObjectHash Target { get; init; }

    /// <inheritdoc />
    public override EntryKind Kind => EntryKind.Redaction;

    /// <inheritdoc />
    public override IReadOnlyList<ObjectHash> References => [];

    /// <inheritdoc />
    public override ObjectHash Redacts => Target;

    internal static RedactionPayload Read(ref WireReader reader) => new() { Target = ObjectHash.Read(ref reader) };

    internal void Write(ref WireWriter writer) => Target.Write(ref writer);
}
