namespace Axiom.Mesh.Core;

/// <summary>What the store writer did with a storable object, and for a stored Entry its store sequence.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="StoreSeq">The store sequence of an Entry stored now (LOCAL-SPEC.md §5.6); 0 otherwise.</param>
internal readonly record struct AdmitResult(AdmitOutcome Outcome, ulong StoreSeq)
{
    /// <summary>Whether the object is stored now, whether by this command or before.</summary>
    public bool IsStored => Outcome is AdmitOutcome.Stored or AdmitOutcome.AlreadyStored;
}
