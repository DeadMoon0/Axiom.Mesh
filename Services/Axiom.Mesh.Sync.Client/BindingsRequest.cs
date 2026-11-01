using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Sync;

/// <summary><c>bindings</c> (SYNC-SERVICE.md §5.1): every binding of the service, by directory. Empty.</summary>
public sealed record BindingsRequest : ISyncMethod<BindingsRequest, BindingsReply>
{
    /// <inheritdoc />
    public static string Method => SyncMethods.Bindings;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out BindingsRequest? value)
    {
        value = reader.Ok ? new BindingsRequest() : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
    }
}
