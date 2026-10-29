namespace Axiom.Mesh.Core;

/// <summary>What <see cref="DialPolicy"/> decided for one announcement: dial, or skip and why.</summary>
/// <param name="Reason"><see cref="SkipReason.None"/> to dial; otherwise why not.</param>
internal readonly record struct DialDecision(SkipReason Reason)
{
    /// <summary>Dial the announced address now.</summary>
    public static DialDecision Dial => default;

    /// <summary>Whether to dial.</summary>
    public bool ShouldDial => Reason == SkipReason.None;

    /// <summary>Skip, for a reason.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>The decision.</returns>
    public static DialDecision Skip(SkipReason reason) => new(reason);

    /// <inheritdoc />
    public override string ToString() => ShouldDial ? "dial" : "skip: " + Reason;
}
