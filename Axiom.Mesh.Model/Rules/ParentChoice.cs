namespace Axiom.Mesh;

/// <summary>What <see cref="ParentRule.Choose"/> decided: write with these parents and this clock, or stop writing.</summary>
/// <remarks>A closed set: <see cref="Write"/> or <see cref="ClockExhausted"/>.</remarks>
public abstract class ParentChoice
{
    private ParentChoice()
    {
    }

    /// <summary>Write the Entry with <see cref="Clock"/> and <see cref="Parents"/>; it is well-formed and faultless.</summary>
    public sealed class Write : ParentChoice
    {
        internal Write(ulong clock, EntryParents parents)
        {
            Clock = clock;
            Parents = parents;
        }

        /// <summary>One more than the highest clock held for the Package, or 0 (SPEC.md §9.3).</summary>
        public ulong Clock { get; }

        /// <summary>The parents (SPEC.md §9.3; LOCAL-SPEC.md §9.4).</summary>
        public EntryParents Parents { get; }

        /// <inheritdoc />
        public override string ToString() => $"Write at clock {Clock} with {Parents.Count} parents";
    }

    /// <summary>
    /// The next clock would be 2^63 or more, which is not well-formed: the device must stop writing to the Package and
    /// say so to the user (SPEC.md §9.3; LOCAL-SPEC.md §9.4 <c>ClockExhausted</c>).
    /// </summary>
    public sealed class ClockExhausted : ParentChoice
    {
        internal ClockExhausted()
        {
        }

        /// <inheritdoc />
        public override string ToString() => "ClockExhausted";
    }
}
