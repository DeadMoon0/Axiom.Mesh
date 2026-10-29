namespace Axiom.Mesh.Protocol;

/// <summary>What the pairing shell sends after a <see cref="PairingMachine"/> step.</summary>
public enum PairingAction
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Send <see cref="PairConfirm.Plain"/>.</summary>
    SendConfirm = 1,

    /// <summary>
    /// Mode <see cref="PairingMode.AddDevice"/>: sign the Roster of <see cref="PairingRules.TryEnrol"/> with the user key,
    /// send it in <see cref="PairConfirm.Enrolling"/>, and store it as the user's current Roster.
    /// </summary>
    SendEnrolment = 2,

    /// <summary>Send <see cref="PairingStep.Cancel"/>, if a pairing session is open, and close it.</summary>
    SendCancel = 3,
}
