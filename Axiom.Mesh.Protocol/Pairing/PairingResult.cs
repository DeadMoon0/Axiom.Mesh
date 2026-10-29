namespace Axiom.Mesh.Protocol;

/// <summary>
/// How a <see cref="PairingMachine"/> took a user's command, as the local protocol answers <c>ConfirmPairing</c> and
/// <c>CancelPairing</c> (LOCAL-SPEC.md §6.2).
/// </summary>
public enum PairingResult
{
    /// <summary>Taken, or a confirmation repeated while <see cref="PairingState.Confirming"/>, which changes nothing.</summary>
    Ok = 0,

    /// <summary>Not allowed in this state: a confirmation while <see cref="PairingState.Waiting"/>, or a peer event out of order, which is ignored.</summary>
    Invalid = 1,

    /// <summary>The pairing has ended.</summary>
    NotFound = 2,
}
