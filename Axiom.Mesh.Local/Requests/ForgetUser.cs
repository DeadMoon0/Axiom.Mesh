using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ForgetUser</c> (277, LOCAL-SPEC.md §6.2): Withdraws pairing status from a known user; a user-key request.</summary>
public sealed record ForgetUser : LocalRequest, IRequest<ForgetUser, NoContent>
{
    /// <summary>The message id, 277 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 277;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ForgetUser";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.2).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The user to forget.</summary>
    public required UserId UserId { get; init; }

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ForgetUser? value)
    {
        var read = new ForgetUser
        {
            UserId = UserId.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        UserId.Write(ref writer);
    }
}
