using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Chat;

/// <summary>What <see cref="PayloadCheck"/> found in a Message Entry.</summary>
/// <param name="Payload">The parsed payload; <see langword="null"/> when it does not parse.</param>
/// <param name="Broken">Whether the Entry is the writer's fault (PAYLOADS.md §1.2, §1.3).</param>
internal sealed record MessageCheck(MessagePayload? Payload, bool Broken);
