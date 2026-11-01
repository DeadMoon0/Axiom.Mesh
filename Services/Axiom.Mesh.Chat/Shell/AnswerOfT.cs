using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>What a handler answers: its reply, or the refusal it ends in. Made by the two conversions only.</summary>
/// <typeparam name="T">The reply record.</typeparam>
internal readonly struct Answer<T>
    where T : class
{
    private Answer(T? reply, CallReply? refusal)
    {
        Reply = reply;
        Refusal = refusal;
    }

    /// <summary>The reply; <see langword="null"/> for a refusal.</summary>
    public T? Reply { get; }

    /// <summary>The refusal; <see langword="null"/> for a reply.</summary>
    public CallReply? Refusal { get; }

    /// <summary>A reply.</summary>
    /// <param name="reply">The reply.</param>
    public static implicit operator Answer<T>(T reply) => new(reply, null);

    /// <summary>A refusal.</summary>
    /// <param name="refusal">The refusal.</param>
    public static implicit operator Answer<T>(CallReply refusal) => new(null, refusal);

    /// <inheritdoc />
    public override string ToString() => Reply is null ? "refused" : "answered";
}
