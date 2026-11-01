using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary>The reply to <c>unbind</c> (SYNC-SERVICE.md §5.3): the binding as it was when it ended.</summary>
public sealed record UnbindReply : IWireRecord<UnbindReply>
{
    /// <summary>The binding, last seen.</summary>
    public Binding Binding { get; init; } = new();

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out UnbindReply? value)
    {
        var read = new UnbindReply { Binding = SyncWire.ReadRecord<Binding>(ref reader) };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer) => Binding.Write(ref writer);
}
