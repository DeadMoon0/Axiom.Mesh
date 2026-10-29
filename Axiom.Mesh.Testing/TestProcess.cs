using Axiom.Mesh.Core;

namespace Axiom.Mesh.Testing;

/// <summary>
/// The process a test pretends to connect from (LOCAL-SPEC.md §2.2): a process id, an operating-system user and an
/// executable, handed to the local port as a pipe listener would read them. <see cref="Served"/> runs as the user every
/// test mesh's Host serves; <see cref="AsOtherUser"/> as somebody else.
/// </summary>
public sealed record TestProcess
{
    /// <summary>The user every node of a test mesh serves.</summary>
    public const string ServedUser = "tester";

    /// <summary>A process of the served user: pid 4242, <c>/usr/bin/axiom-test</c>.</summary>
    public static TestProcess Served { get; } = new() { Pid = 4242, User = ServedUser, Executable = "/usr/bin/axiom-test" };

    /// <summary>The process id.</summary>
    public required int Pid { get; init; }

    /// <summary>The operating-system user.</summary>
    public required string User { get; init; }

    /// <summary>The executable's path.</summary>
    public required string Executable { get; init; }

    /// <summary>The identity the local port receives.</summary>
    public ProcessIdentity Identity => ProcessIdentity.Pipe(Pid, User, Executable);

    /// <summary>The same process, run by another operating-system user than the one the Host serves.</summary>
    /// <returns>The process.</returns>
    public TestProcess AsOtherUser() => this with { User = "somebody-else" };

    /// <summary>The same user, another program.</summary>
    /// <param name="executable">The program's path.</param>
    /// <param name="pid">Its process id.</param>
    /// <returns>The process.</returns>
    public TestProcess Running(string executable, int pid) => this with { Executable = executable, Pid = pid };
}
