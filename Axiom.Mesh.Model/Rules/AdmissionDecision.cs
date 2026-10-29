namespace Axiom.Mesh;

/// <summary>
/// What <see cref="EntryAdmission.Decide"/> decided: <see cref="Admit"/>, <see cref="Unverifiable"/> or
/// <see cref="Invalid"/>. A closed set.
/// </summary>
public abstract class AdmissionDecision
{
    private AdmissionDecision()
    {
    }

    /// <summary>The Entry is valid: store it. Once stored it stays valid (SPEC.md §9.6).</summary>
    public sealed class Admit : AdmissionDecision
    {
        internal Admit(AdmittedEntry entry) => Entry = entry;

        /// <summary>The storable Entry.</summary>
        public AdmittedEntry Entry { get; }

        /// <inheritdoc />
        public override string ToString() => $"Admit {Entry.Hash}";
    }

    /// <summary>
    /// Something the decision needs is not held yet: hold the Entry (bounded) and decide again when it arrives
    /// (SPEC.md §8.1, §10.6). Never stored, served or forwarded before.
    /// </summary>
    public sealed class Unverifiable : AdmissionDecision
    {
        internal Unverifiable(Prerequisite need, UserId writer)
        {
            Need = need;
            Writer = writer;
        }

        /// <summary>What is missing.</summary>
        public Prerequisite Need { get; }

        /// <summary>For <see cref="Prerequisite.WritersGrant"/>, the user whose Grant is missing; zero otherwise.</summary>
        public UserId Writer { get; }

        /// <inheritdoc />
        public override string ToString() => $"Unverifiable: needs {Need}";
    }

    /// <summary>The Entry is invalid: drop it and record its sender as faulty for it (SPEC.md §12.2).</summary>
    public sealed class Invalid : AdmissionDecision
    {
        internal Invalid(AdmissionRefusal reason) => Reason = reason;

        /// <summary>Why.</summary>
        public AdmissionRefusal Reason { get; }

        /// <inheritdoc />
        public override string ToString() => $"Invalid: {Reason}";
    }
}
