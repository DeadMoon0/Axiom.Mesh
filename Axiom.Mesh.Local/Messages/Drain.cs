using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// <c>Drain</c> (6, LOCAL-SPEC.md §4.5): the Host is about to stop. The client finishes what it can within the grace and
/// sends <c>Bye(Done)</c>; new requests are answered <c>Draining</c>; when the grace elapsed the Host sends
/// <c>Bye(Shutdown)</c>.
/// </summary>
public sealed class Drain : LocalMessage, IHostMessage
{
    /// <summary>Milliseconds the Host waits, 1 to 30 000; 5 000 by default.</summary>
    public uint GraceMilliseconds { get; init; } = Limits.DrainGraceDefaultMilliseconds;

    /// <inheritdoc />
    public override ushort MessageId => LocalMessageIds.Drain;

    /// <inheritdoc />
    public override string ToString() => $"Drain({GraceMilliseconds} ms)";

    internal static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Drain? message)
    {
        var read = new Drain { GraceMilliseconds = reader.UInt32BigEndian() };
        message = reader.Ok ? read : null;
        return message is not null;
    }

    internal override void WritePayload(ref WireWriter writer) => writer.UInt32BigEndian(GraceMilliseconds);
}
