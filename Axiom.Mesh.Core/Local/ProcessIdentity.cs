using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// Who is at the other end of a local connection (LOCAL-SPEC.md §2.2): for a program on the pipe, its process id, its
/// operating-system user and its executable, as the Host's pipe listener read them from the operating system; for an
/// in-process service, the service's name. Required by <see cref="LocalPort.AdoptAsync"/>, so no connection is served
/// without one.
/// </summary>
/// <remarks>
/// The identity decides nothing in the engine beyond what LOCAL-SPEC.md says: an in-process service's name is granted
/// only on its own in-memory pair (§4.2 check 3), and every user-key request is logged with <see cref="Pid"/> and
/// <see cref="Executable"/> and may be confined to listed executables (§2.2, §13). Whether the OS user may connect at
/// all is the Host's decision, made before it adopts the connection.
/// </remarks>
[ClosedShape("LOCAL-SPEC.md §2.2 names exactly the process id, the user and the executable; the in-process service is the other provenance of §4.2.")]
public sealed record ProcessIdentity
{
    private ProcessIdentity(ProcessKind kind, int pid, string user, string executable, string service)
    {
        Kind = kind;
        Pid = pid;
        User = user;
        Executable = executable;
        Service = service;
    }

    /// <summary>How the connection reached the port.</summary>
    public ProcessKind Kind { get; }

    /// <summary>The peer's process id; 0 for an in-process service or where the platform does not say.</summary>
    public int Pid { get; }

    /// <summary>The peer's operating-system user as the platform names it (Windows: <c>DOMAIN\name</c>; Linux: the uid); empty in process.</summary>
    public string User { get; }

    /// <summary>The peer's executable, an absolute path; empty in process or where the platform does not say.</summary>
    public string Executable { get; }

    /// <summary>The in-process service this connection was opened for; empty for a program on the pipe.</summary>
    public string Service { get; }

    /// <summary>A program on the pipe.</summary>
    /// <param name="pid">Its process id; 0 when the platform does not say.</param>
    /// <param name="user">Its operating-system user, never empty.</param>
    /// <param name="executable">Its executable's path; empty when the platform does not say.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="ArgumentException"><paramref name="user"/> is empty, or <paramref name="pid"/> is negative.</exception>
    public static ProcessIdentity Pipe(int pid, string user, string executable)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentOutOfRangeException.ThrowIfNegative(pid);
        return new ProcessIdentity(ProcessKind.Pipe, pid, user, executable, string.Empty);
    }

    /// <summary>An in-process service on its in-memory pair (LOCAL-SPEC.md §4.2).</summary>
    /// <param name="service">The service's name, as the configuration lists it.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="ArgumentException"><paramref name="service"/> is empty.</exception>
    public static ProcessIdentity InProcess(string service)
    {
        ArgumentException.ThrowIfNullOrEmpty(service);
        return new ProcessIdentity(ProcessKind.InProcess, 0, string.Empty, string.Empty, service);
    }

    /// <inheritdoc />
    public override string ToString() => Kind == ProcessKind.InProcess
        ? $"in-process service \"{Service}\""
        : $"pid {Pid} \"{Executable}\" as \"{User}\"";
}
