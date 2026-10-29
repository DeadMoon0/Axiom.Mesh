using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>Unsubscribe</c> (401, LOCAL-SPEC.md §7.1): Ends a subscription; no event of it follows the response.</summary>
public sealed record Unsubscribe : LocalRequest, IRequest<Unsubscribe, NoContent>
{
    /// <summary>The message id, 401 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 401;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "Unsubscribe";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §7.1).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The subscription.</summary>
    public required uint SubscriptionId { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out NoContent ok)
    {
        ok = default;
        return true;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, NoContent ok)
    {
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Unsubscribe? value)
    {
        var read = new Unsubscribe
        {
            SubscriptionId = reader.UInt32BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt32BigEndian(SubscriptionId);
    }
}
