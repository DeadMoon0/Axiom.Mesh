namespace Axiom.Mesh.Core;

/// <summary>An object received but not yet verifiable (SPEC.md §1.2 "Held"): never stored, served, pinned or acted on.</summary>
/// <param name="Object">The parsed object; its bytes are a copy intake owns.</param>
/// <param name="Origin">Who sent it.</param>
/// <param name="ReceivedMs">When it arrived, in milliseconds since the Unix epoch.</param>
/// <param name="Need">What it waits for.</param>
/// <param name="Context">The Package it was wanted for, when known.</param>
internal sealed record HeldObject(ParsedObject Object, Origin Origin, long ReceivedMs, HeldNeed Need, PackageId? Context)
{
    /// <summary>Its length, counted against the bounds.</summary>
    public long Bytes => Object.Bytes.Length;
}
