<identity>
    <package>Axiom.Mesh.Protocol</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Model.SKILL.md</requires>
</identity>

<objective>
    Teach an agent AMP/1 as Axiom.Mesh describes it with ProtoStream, and the pairing protocol.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Protocol. Today: Amp.Alpn. Planned (M2): MeshMessage and its 16 message classes plus Extension, Amp.Definition with a hand-written frame codec (u16 ids; 17 to 255 ignored as Extension), HelloRule, ListingRule, WantBudget, PairProtocol, PairingMachine, Sas. Status: phase 1a skeleton; read the package README for what exists today. Every rule cites the section of SPEC.md or LOCAL-SPEC.md it implements, and its tests take expected values from that section.
</package_scope>

<architecture>
    Honest: definitions, codecs and pure rules. Depends on Model and ProtoStream. Unknown message ids go through .Framing(...).FrameCodec(...), not .Messages(...), so ids 17 to 255 are ignored as SPEC.md 1.4 says (plan decision D5).
</architecture>

<best_practices>
    Round-trip every message at every byte split with ProtoStream.Testing's CodecHarness; refuse every limit one over and accept it at the limit. Rules take timestamps, never a TimeProvider.
</best_practices>

<pitfalls>
    A violation closes with Bye; faulty content does not. Keep the two apart (SPEC.md 12).
</pitfalls>
