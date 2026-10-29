namespace Axiom.Mesh.Protocol;

/// <summary>What <see cref="ListingRule"/> decided about a received <c>Packages</c> or <c>PackageChanged</c>.</summary>
public enum ListingVerdict
{
    /// <summary>Accepted; a listing in progress continues, or a <c>PackageChanged</c> updates the view.</summary>
    Accepted = 0,

    /// <summary>The last page of a listing arrived in time: replace the view of the sender's state (SPEC.md §7.2).</summary>
    Completed = 1,

    /// <summary>The listing broke a rule of §7.2: close with <c>Bye(Violation)</c> (§12.1).</summary>
    Violation = 2,
}
