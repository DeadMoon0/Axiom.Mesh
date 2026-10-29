using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>Writes a typed body held as an object, so that a frame built to be sent needs no buffer of its own.</summary>
/// <param name="body">The body.</param>
/// <param name="writer">The writer.</param>
internal delegate void BodyWriter(object body, ref WireWriter writer);
