namespace Axiom.Mesh.Host;

/// <summary>What <see cref="PipeAdmission.Decide"/> decided: admit the connection, or refuse it for a reason the log keeps.</summary>
internal sealed class PipeAdmissionVerdict
{
    private PipeAdmissionVerdict(string? refusal) => Refusal = refusal;

    /// <summary>Admit: the local port takes the connection.</summary>
    public static PipeAdmissionVerdict Admit { get; } = new(null);

    /// <summary>Why the connection is refused; <see langword="null"/> when admitted.</summary>
    public string? Refusal { get; }

    /// <summary>Whether the connection is admitted.</summary>
    public bool IsAdmitted => Refusal is null;

    /// <summary>Refuses: the connection is closed before <c>Hello</c> is answered.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>The verdict.</returns>
    public static PipeAdmissionVerdict Refuse(string reason) => new(reason);

    /// <inheritdoc />
    public override string ToString() => Refusal is null ? "admit" : "refuse: " + Refusal;
}
