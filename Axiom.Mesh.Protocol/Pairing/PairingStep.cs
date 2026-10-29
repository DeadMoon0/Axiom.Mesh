namespace Axiom.Mesh.Protocol;

/// <summary>One step of a <see cref="PairingMachine"/>: how the event was taken, the machine after it, and what to send.</summary>
public sealed class PairingStep
{
    internal PairingStep(PairingResult result, PairingMachine next, PairingAction action, PairCancel? cancel)
    {
        Result = result;
        Next = next;
        Action = action;
        Cancel = cancel;
    }

    /// <summary>How the event was taken.</summary>
    public PairingResult Result { get; }

    /// <summary>The machine after the event; the same one when nothing changed.</summary>
    public PairingMachine Next { get; }

    /// <summary>What to send.</summary>
    public PairingAction Action { get; }

    /// <summary>For <see cref="PairingAction.SendCancel"/>, the message; <see langword="null"/> otherwise.</summary>
    public PairCancel? Cancel { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Result}: {Next.State}, {Action}";
}
