namespace Axiom.Mesh.Protocol;

/// <summary>One step of <see cref="ListingRule"/>: the verdict and the rule's state after it.</summary>
public readonly struct ListingStep
{
    internal ListingStep(ListingVerdict verdict, ListingRule next, string detail)
    {
        Verdict = verdict;
        Next = next;
        Detail = detail;
    }

    /// <summary>What was decided.</summary>
    public ListingVerdict Verdict { get; }

    /// <summary>The rule's state for the next message from the same peer.</summary>
    public ListingRule Next { get; }

    /// <summary>For <see cref="ListingVerdict.Violation"/>, which rule was broken; empty otherwise.</summary>
    public string Detail { get; }

    /// <summary>For a violation, the closing message; <see langword="null"/> otherwise.</summary>
    public Bye? Bye => Verdict == ListingVerdict.Violation ? new Bye(ByeReason.Violation, Detail) : null;

    /// <inheritdoc />
    public override string ToString() => Verdict == ListingVerdict.Violation ? $"Violation: {Detail}" : Verdict.ToString();
}
