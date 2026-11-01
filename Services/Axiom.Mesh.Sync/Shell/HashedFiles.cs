using System.Collections.Generic;

namespace Axiom.Mesh.Sync;

/// <summary>What reading the changed files of a run gave (<see cref="FileHasher"/>).</summary>
/// <param name="Prints">The fingerprint of every file read: its size and write time before the read, the ChunkList its bytes became.</param>
/// <param name="Problems">Files not published: too large, unreadable.</param>
/// <param name="Puts">Every object put, whose pin the service holds until the run ends.</param>
/// <param name="Vanished">Files gone between the scan and the read.</param>
/// <param name="Busy">Files that changed while they were read: read at a later run.</param>
internal sealed record HashedFiles(IReadOnlyList<Fingerprint> Prints, IReadOnlyList<ScanProblem> Problems, IReadOnlyList<ObjectHash> Puts, IReadOnlyList<string> Vanished, IReadOnlyList<string> Busy);
