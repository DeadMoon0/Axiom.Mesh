using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>Subscribe</c> (400, LOCAL-SPEC.md §7.1): Starts a subscription; <c>LocalClient.SubscribeAsync</c> sends it for you.</summary>
public sealed record Subscribe : LocalRequest, IRequest<Subscribe, NoContent>
{
    /// <summary>The message id, 400 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 400;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "Subscribe";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §7.1).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>Chosen by the client, never 0, unique among the connection's live subscriptions.</summary>
    public required uint SubscriptionId { get; init; }

    /// <summary>The topic (§14.5).</summary>
    public required Topic Topic { get; init; }

    /// <summary>What of the topic to receive; fields the topic does not read MUST be zero or empty.</summary>
    public required Filter Filter { get; init; }

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Subscribe? value)
    {
        var read = new Subscribe
        {
            SubscriptionId = reader.UInt32BigEndian(),
            Topic = (Topic)reader.UInt16BigEndian(),
            Filter = LocalWire.ReadRecord<Filter>(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt32BigEndian(SubscriptionId);
        writer.UInt16BigEndian((ushort)Topic);
        Filter.Write(ref writer);
    }
}
