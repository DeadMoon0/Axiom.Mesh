using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>What intake made of one received object.</summary>
/// <param name="Verdict">Admitted, held, dropped, faulty or a violation.</param>
/// <param name="Detail">Why it was dropped, faulty or a violation, citing the section; for logs and peer records.</param>
/// <param name="Stored">For an admitted object, the store writer's completion.</param>
/// <param name="Need">For a held object, what it waits for.</param>
internal sealed record IntakeOutcome(IntakeVerdict Verdict, string? Detail, Task<AdmitResult>? Stored, HeldNeed Need);
