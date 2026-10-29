using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>Why a request or a callback failed: the body of every <c>Response</c> and <c>Reply</c> whose status is not <c>Ok</c> (LOCAL-SPEC.md §5.14).</summary>
public sealed record Error : IWireRecord<Error>
{
    /// <summary>For display and logs.</summary>
    public string Message { get; init; } = "";

    /// <summary>For logs, naming the field for <c>Invalid</c>; MUST NOT be interpreted.</summary>
    public string Detail { get; init; } = "";

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Error? value)
    {
        var read = new Error
        {
            Message = Wire.ReadString(ref reader, FieldBounds.ErrorMessageLength),
            Detail = Wire.ReadString(ref reader, FieldBounds.ErrorDetailLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Message, FieldBounds.ErrorMessageLength);
        Wire.WriteString(ref writer, Detail, FieldBounds.ErrorDetailLength);
    }
}
