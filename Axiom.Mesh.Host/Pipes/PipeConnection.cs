using System.IO;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Host;

/// <summary>A connection on the pipe and its peer's process identity (LOCAL-SPEC.md §2.2).</summary>
/// <param name="Stream">The connection; whoever takes it owns it.</param>
/// <param name="Identity">Who is at the other end.</param>
internal sealed record PipeConnection(Stream Stream, ProcessIdentity Identity);
