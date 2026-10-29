namespace Axiom.Mesh.Core;

/// <summary>What <see cref="IntakeRules.Decide"/> decided about one parsed object.</summary>
/// <param name="Verdict">Store (<see cref="IntakeVerdict.Admitted"/>), hold, drop, or faulty.</param>
/// <param name="Storable">The object to store, for <see cref="IntakeVerdict.Admitted"/>.</param>
/// <param name="Need">What a held object waits for.</param>
/// <param name="Detail">Why it was dropped or is faulty, citing the section.</param>
internal readonly record struct IntakeDecision(IntakeVerdict Verdict, StorableObject? Storable, HeldNeed Need, string? Detail)
{
    /// <summary>Store it.</summary>
    /// <param name="storable">The object.</param>
    /// <returns>The decision.</returns>
    public static IntakeDecision Store(StorableObject storable) => new(IntakeVerdict.Admitted, storable, default, null);

    /// <summary>Hold it.</summary>
    /// <param name="kind">What it waits for.</param>
    /// <param name="subject">Whose.</param>
    /// <returns>The decision.</returns>
    public static IntakeDecision Hold(HeldNeedKind kind, Hash32 subject) => new(IntakeVerdict.Held, null, new HeldNeed(kind, subject), null);

    /// <summary>Drop it without blame.</summary>
    /// <param name="detail">Why.</param>
    /// <returns>The decision.</returns>
    public static IntakeDecision Drop(string detail) => new(IntakeVerdict.Dropped, null, default, detail);

    /// <summary>Drop it as faulty content.</summary>
    /// <param name="detail">Why, citing the section.</param>
    /// <returns>The decision.</returns>
    public static IntakeDecision Faulty(string detail) => new(IntakeVerdict.Faulty, null, default, detail);
}
