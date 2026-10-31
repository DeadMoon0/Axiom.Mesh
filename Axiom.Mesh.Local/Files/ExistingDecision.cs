namespace Axiom.Mesh.Local;

/// <summary>What to do with the target directory, and why not when the answer is <see cref="ExistingAction.Refuse"/>.</summary>
/// <param name="Action">The action.</param>
/// <param name="Status">For a refusal, the status (<c>AlreadyExists</c> or <c>Invalid</c>); else <c>Ok</c>.</param>
/// <param name="Problem">For a refusal, the explanation; else empty.</param>
internal readonly record struct ExistingDecision(ExistingAction Action, Status Status, string Problem);
