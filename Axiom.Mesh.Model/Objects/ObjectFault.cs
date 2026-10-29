namespace Axiom.Mesh;

/// <summary>
/// Content a peer sent that is dropped, never stored or forwarded, with its sender recorded as faulty for it
/// (SPEC.md §12.2). A value, never an exception: a peer's mistake is not the caller's.
/// </summary>
/// <param name="Kind">Malformed or faulty.</param>
/// <param name="Detail">What was wrong, citing the section, for logs; never interpreted.</param>
[ClosedShape("A fault is a kind and a detail; SPEC.md §12.2 knows no more.")]
public readonly record struct ObjectFault(ObjectFaultKind Kind, string Detail)
{
    internal static ObjectFault Malformed(string detail) => new(ObjectFaultKind.Malformed, detail);

    internal static ObjectFault Faulty(string detail) => new(ObjectFaultKind.Faulty, detail);

    /// <inheritdoc />
    public override string ToString() => $"{Kind}: {Detail}";
}
