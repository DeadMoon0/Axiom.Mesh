using System;

namespace Axiom.Mesh.Host;

/// <summary>How a spawned service's process ended: its exit code, how long it ran, and whether the daemon killed it.</summary>
/// <param name="ExitCode">The process's exit code.</param>
/// <param name="RanFor">From its start to its exit.</param>
/// <param name="Killed">The daemon killed it after its stop grace.</param>
internal sealed record ChildExit(int ExitCode, TimeSpan RanFor, bool Killed);
