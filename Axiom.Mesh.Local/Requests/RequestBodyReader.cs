using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>Reads the body of one request class; the reader is positioned after the correlation.</summary>
/// <param name="reader">The reader.</param>
/// <param name="request">The request, or <see langword="null"/> when a field was outside its bounds.</param>
/// <returns><see langword="true"/> when the body was read.</returns>
internal delegate bool RequestBodyReader(ref WireReader reader, out LocalRequest? request);
